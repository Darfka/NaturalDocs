﻿/* 
 * Class: CodeClear.NaturalDocs.Engine.CodeDB.Manager
 * ____________________________________________________________________________
 * 
 * A class to manage information about various aspects of the code and its documentation.
 * 
 * 
 * Topic: Usage
 * 
 *		- Register any change watching objects you desire with <AddChangeWatcher()>.
 * 
 *		- Call <Engine.Instance.Start()> which will start this module.
 *		
 *		- Call <GetAccessor()> or <GetPriorityAccessor()> to create objects which will be used to manipulate the database.
 *		  Each thread must have their own.
 *		  
 *		- The change watchers will receive notifications of any modifications the accessors perform.  They can be added and
 *		  removed while the module is running.
 *		  
 *		- Each <Accessor> must be disposed before disposing of the database manager.
 *		
 *		- Disposing of the manager will automatically call <Cleanup()>, though if you have some idle time in which the 
 *		  documentation is completely updated you may call it ahead of time.
 *		  
 * 
 * Multithreading: Thread Safety Notes
 * 
 *		> DatabaseLock -> ChangeWatchers
 * 
 *		Externally, this class is thread safe so long as each thread uses its own <Accessor>.
 *		
 *		For the <Accessor> implementation, all uses of the database connection must be managed by <DatabaseLock>.  
 *		<UsedTopicIDs> and <UsedContextIDs> are only relevant when making changes to the database, so they are 
 *		managed by <DatabaseLock> as well.
 *		
 *		The change watchers, on the other hand, have their own lock since they may be accessed independently.  You may 
 *		attempt to acquire the list with <LockChangeWatchers()> while holding <DatabaseLock>, but not vice versa.
 *		
 * 
 * Topic: Used IDs and Transactions
 * 
 *		At the moment, ID tracking number sets such as <UsedTopicIDs> don't support transactions correctly.  If you were to
 *		add a topic to the database as part of a transaction and then roll it back instead of committing it, the IDs would still
 *		be marked as used.  This has the potential to eat up all the available IDs if a database is used over a long period of time 
 *		without a full rebuild ever being performed.
 *		
 *		This is not being fixed, however, because it's assumed that rolling back transactions never happens in Natural Docs as 
 *		part of a normal path of execution.  Transactions are used mostly for performance and just as good practice in case
 *		this assumption should change in the future.  The only time it should occur is if the program crashes and it's triggered
 *		automatically.  However, in this case the database will be completely rebuilt on the next execution anyway so we don't 
 *		need to worry about it.
 */

// This file is part of Natural Docs, which is Copyright © 2003-2017 Code Clear LLC.
// Natural Docs is licensed under version 3 of the GNU Affero General Public License (AGPL)
// Refer to License.txt for the complete details


using System;
using System.Collections.Generic;
using CodeClear.NaturalDocs.Engine.Collections;
using CodeClear.NaturalDocs.Engine.Languages;
using CodeClear.NaturalDocs.Engine.Links;
using CodeClear.NaturalDocs.Engine.Symbols;
using CodeClear.NaturalDocs.Engine.Tokenization;
using CodeClear.NaturalDocs.Engine.Topics;
using CodeClear.NaturalDocs.Engine.CommentTypes;


namespace CodeClear.NaturalDocs.Engine.CodeDB
	{
	public partial class Manager : Module
		{
		
		// Group: Functions
		// __________________________________________________________________________
		
		
		/* Function: Manager
		 */
		public Manager (Engine.Instance engineInstance) : base (engineInstance)
			{
			connection = null;
			databaseLock = new Lock();

			usedTopicIDs = new IDObjects.NumberSet();
			usedLinkIDs = new IDObjects.NumberSet();
			usedClassIDs = new IDObjects.NumberSet();
			usedContextIDs = new IDObjects.NumberSet();

			linksToResolve = new IDObjects.NumberSet();
			newTopicsByEndingSymbol = new SafeDictionary<Symbols.EndingSymbol, IDObjects.NumberSet>();

			classIDReferenceChangeCache = new ReferenceChangeCache();
			contextIDReferenceChangeCache = new ReferenceChangeCache();
			
			changeWatchers = new List<IChangeWatcher>();
			reparsingEverything = false;
			}
			
			
		/* Function: AddChangeWatcher
		 * Adds an object to be notified about changes to the database.  This can be called both before and after
		 * <Start()>.
		 */
		public void AddChangeWatcher (IChangeWatcher watcher)
			{
			lock (changeWatchers)
				{
				changeWatchers.Add(watcher);
				}
			}
			
			
		/* Function: AddPriorityChangeWatcher
		 * Adds an object to be notified about changes to the database.  Ones added with this function will receive
		 * change notifications before ones that aren't.  This can be called both before and after <Start()>.
		 */
		public void AddPriorityChangeWatcher (IChangeWatcher watcher)
			{
			lock (changeWatchers)
				{
				changeWatchers.Insert(0, watcher);
				}
			}
			
			
		/* Function: RemoveChangeWatcher
		 * Removes a watcher so that they're no longer notified of changes to the database.  It doesn't matter which
		 * function you used to add it with.  This can be called both before and after <Start()>.
		 */
		public void RemoveChangeWatcher (IChangeWatcher watcher)
			{
			lock (changeWatchers)
				{
				for (int i = 0; i < changeWatchers.Count; i++)
					{
					if ((object)watcher == (object)changeWatchers[i])
						{
						changeWatchers.RemoveAt(i);
						return;
						}
					}
				}
			}
			
			
		/* Function: Start
		 * 
		 * Dependencies:
		 * 
		 *		- <Config.Manager> must be started before using the rest of the class.
		 */
		public bool Start (Errors.ErrorList errors)
			{
			SQLite.API.Result sqliteResult = SQLite.API.Initialize();
			
			if (sqliteResult != SQLite.API.Result.OK)
			    {  throw new SQLite.Exceptions.UnexpectedResult("Could not initialize SQLite.", sqliteResult);  }

			Path databaseFile = EngineInstance.Config.WorkingDataFolder + "/CodeDB.nd";
			connection = new SQLite.Connection();
			bool success = false;
			
			if (EngineInstance.Config.ReparseEverything == false)
				{
				try
					{
					connection.Open(databaseFile, false);
					
					Version version = GetVersion();
					
					if (Version.BinaryDataCompatibility(version, Engine.Instance.Version, "2.0") == true)
						{  
						LoadSystemVariables();
						success = true;
						}
					}
				catch { }
				}
			
			if (!success)
				{
				connection.Dispose();
				
				if (System.IO.File.Exists(databaseFile))
					{  System.IO.File.Delete(databaseFile);  }
					
				EngineInstance.Config.ReparseEverything = true;
				reparsingEverything = true;
					
				connection.Open(databaseFile, true);
				CreateDatabase();
				}
				
			return true;
			}
			
			
		/* Function: GetAccessor
		 * Creates an <Accessor> for manipulating the database.  Each thread must have its own.
		 */
		public Accessor GetAccessor ()
			{
			return new Accessor(this, connection.CreateAnotherConnection(), false);
			}
			
			
		/* Function: GetPriorityAccessor
		 * Creates an <Accessor> for manipulating the database which takes priority over other Accessors whenever possible.  This
		 * is useful for interface related threads that should have greater priority than background workers.
		 */
		public Accessor GetPriorityAccessor ()
			{
			return new Accessor(this, connection.CreateAnotherConnection(), true);
			}


		/* Function: Dispose
		 */
		protected override void Dispose (bool strictRulesApply)
			{
			// If strict rules apply then the connection object will have to dispose of itself.  We can't do it here.
			if (!strictRulesApply && connection != null && connection.IsOpen)
				{
				if (databaseLock.IsLocked)
					{  throw new Exception("Attempted to dispose of database when there were still locks held.");  }
				
				Cleanup(Delegates.NeverCancel);
				SaveSystemVariablesAndVersion();
					
				connection.Dispose();
				connection = null;

				usedTopicIDs.Clear();
				usedLinkIDs.Clear();
				usedClassIDs.Clear();
				usedContextIDs.Clear();

				linksToResolve.Clear();
				newTopicsByEndingSymbol.Clear();

				classIDReferenceChangeCache.Clear();
				contextIDReferenceChangeCache.Clear();
				
				SQLite.API.Result shutdownResult = SQLite.API.ShutDown();

				if (shutdownResult != SQLite.API.Result.OK)
					{  throw new SQLite.Exceptions.UnexpectedResult("Could not shut down SQLite.", shutdownResult);  }
				}
			}
			
			
		/* Function: Cleanup
		 * 
		 * Cleans up any stray data associated with the database, assuming all documentation is up to date.  You can pass a
		 * <CancelDelegate> if you'd like to interrupt this process early.
		 * 
		 * <Dispose()> will call this function automatically so it's not strictly necessary to call it manually, though it's good
		 * practice to.  If you have idle time in which the documentation is completely up to date, calling this then instead of
		 * leaving it for <Dispose()> will allow the engine to shut down faster.
		 */
		public void Cleanup (CancelDelegate cancelDelegate)
			{
			using (Accessor accessor = GetAccessor())
				{
				accessor.GetReadPossibleWriteLock();
				accessor.FlushClassIDReferenceChangeCache(cancelDelegate);
				accessor.FlushContextIDReferenceChangeCache(cancelDelegate);
				accessor.ReleaseLock();
				}
			}



		// Group: Link Functions
		// __________________________________________________________________________


		/* Function: ScoreLink
		 * 
		 * Generates a numeric score representing how well the <Topic> serves as a match for the <Link>.  Higher scores are
		 * better, and zero means they don't match at all.
		 * 
		 * If a score has to beat a certain threshold to be relevant, you can pass it to lessen the processing load.  This function 
		 * may be able to tell it can't beat the score early and return without performing later steps.  In these cases it will return 
		 * -1.
		 * 
		 * If scoring a Natural Docs link you must pass a list of interpretations.  It must include the literal form.
		 */
		public long ScoreLink (Link link, Topic topic, long minimumScore = 0, List<LinkInterpretation> interpretations = null)
			{
			// DEPENDENCY: These functions depend on the score's internal format:
			//    - CodeDB.Manager.ScoreInterpretation(), ScoreScopeIntertpretation(), ScoreUsingInterpretation()
			//    - CodeDB.Manager.GetInterpretationIndex()
			//    - CodeDB.Manager.ScoreParameter()
			//    - CodeDB.Manager.ScoreTopic()
			//    - EngineTests.LinkScoring

			// Other than that the score's format should be treated as opaque.  Nothing beyond these functions should try to 
			// interpret the value other than to know that higher is better, zero is impossible, and -1 means we quit early.

			// It's a 64-bit value so we'll assign bits to the different characteristics.  Higher order bits obviously result in higher 
			// numeric values so the characteristics are ordered by priority.

			// Format:
			// 0LCETPPP PPPPPPPP PPPPPPPP PSSSSSSS SSSIIIII IBFFFFFF Rbbbbbbb brrrrrr1

			// 0 - The first bit is zero to make sure the number is positive.

			// L - Whether the topic matches the link's language.
			// C - Whether the topic and link's capitalization match if it matters to the language.
			// E - Whether the text is an exact match with no plural or possessive conversions applied.
			// T - Whether the link parameters exactly match the topic title parameters.
			// P - How well the parameters match.
			// S - How high on the scope list the symbol match is.
			// I - How high on the interpretation list (named/plural/possessive) the match is.
			// B - Whether the topic has a body
			// F - How high on the list of topics that define the same symbol in the same file this is.
			// R - Whether the topic has a prototype.
			// b - The length of the body divided by 16.
			// r - The length of the prototype divided by 16.

			// 1 - The final bit is one to make sure a match will never be zero.


			// For type and class parent links, the comment type MUST have the relevant attribute set to be possible.

			var commentType = EngineInstance.CommentTypes.FromID(topic.CommentTypeID);
			var language = EngineInstance.Languages.FromID(topic.LanguageID);

			if ( (link.Type == LinkType.ClassParent && commentType.Flags.ClassHierarchy == false) ||
				  (link.Type == LinkType.Type && commentType.Flags.VariableType == false) )
				{  return 0;  }


			// 0------- -------- -------- -------- -------- -------- -------- -------1
			// Our baseline.

			long score = 0x0000000000000001;


			// =L------ -------- -------- -------- -------- -------- -------- -------=
			// L - Whether the topic's language matches the link's language.  For type and class parent links this is mandatory.  For
			// Natural Docs links this is the highest priority criteria as links should favor any kind of match within their own language
			// over matches from another.

			if (link.LanguageID == topic.LanguageID)
				{  score |= 0x4000000000000000;  }
			else if (link.Type == LinkType.ClassParent || link.Type == LinkType.Type)
				{  return 0;  }
			else if (minimumScore > 0x3FFFFFFFFFFFFFFF)
				{  return -1;  }


			// ==CE---- -------- -------- -SSSSSSS SSSIIIII I------- -------- -------=
			// Now we have to go through the interpretations to figure out the fields that could change based on them.
			// C and S will be handled by ScoreInterpretation().  E and I will be handled here.

			// C - Whether the topic and link's capitalization match if it matters to the language.  This depends on the
			//			interpretation because it can be affected by how named links are split.
			// E - Whether the text is an exact match with no plural or possessive conversions applied.  Named links are
			//			okay.
			// S - How high on the scope list the symbol match is.
			// I - How high on the interpretation list (named/plural/possessive) the match is.

			long bestInterpretationScore = 0;
			int bestInterpretationIndex = 0;

			if (link.Type == LinkType.NaturalDocs)
				{
				for (int i = 0; i < interpretations.Count; i++)
					{
					long interpretationScore = ScoreInterpretation(topic, link, SymbolString.FromPlainText_NoParameters(interpretations[i].Target));

					if (interpretationScore != 0)
						{
						// Add E if there were no plurals or possessives.  Named links are okay.
						if (interpretations[i].PluralConversion == false && interpretations[i].PossessiveConversion == false)
							{  interpretationScore |= 0x1000000000000000;  }

						if (interpretationScore > bestInterpretationScore)
							{  
							bestInterpretationScore = interpretationScore;
							bestInterpretationIndex = i;
							}
						}
					}
				}

			else // type or class parent link
				{
				bestInterpretationScore = ScoreInterpretation(topic, link, link.Symbol);
				bestInterpretationIndex = 0;

				// Add E if there was a match.
				if (bestInterpretationScore != 0)
					{  bestInterpretationScore |= 0x1000000000000000;  }
				}

			// If none of the symbol interpretations matched the topic, we're done.
			if (bestInterpretationScore == 0)
				{  return 0;  }

			// Combine C, E, and S into the main score.
			score |= bestInterpretationScore;

			// Calculate I so that lower indexes are higher scores.  Since these are the lowest order bits it's okay to leave
			// this for the end instead of calculating it for every interpretation.
			if (bestInterpretationIndex > 63)
				{  bestInterpretationIndex = 63;  }

			long bestInterpretationBits = 63 - bestInterpretationIndex;
			bestInterpretationBits <<= 23;

			score |= bestInterpretationBits;

			if ((score | 0x0FFFFF80007FFFFF) < minimumScore)
				{  return -1;  }


			// ====TPPP PPPPPPPP PPPPPPPP P======= ======== =------- -------- -------=
			// T - Whether the link parameters exactly match the topic title parameters.
			// P - How well the parameters match.
			
			// Both of these only apply to Natural Docs links that have parameters.
			if (link.Type == LinkType.NaturalDocs)
				{  
				int parametersIndex = ParameterString.GetParametersIndex(link.Text);

				if (parametersIndex != -1)
					{
					string linkParametersString = link.Text.Substring(parametersIndex);
					ParameterString linkParameters = ParameterString.FromPlainText(linkParametersString);

					// If the topic title has parameters as well, the link parameters must match them exactly.  We
					// don't do fuzzy matching with topic title parameters.
					if (topic.HasTitleParameters && string.Compare(linkParameters, topic.TitleParameters, !language.CaseSensitive) == 0)
						{  
						score |= 0x0800000000000000;
						// We can skip the prototype match since this outweighs it.  Also, we don't want two link targets
						// where the topic title parameters are matched to be distinguished by the prototype parameters.
						// We'll let it fall through to lower properties in the score.
						}
					else
						{
						// Score the first nine parameters.
						for (int i = 0; i < 9; i++)
							{
							long paramScore = ScoreParameter(topic.ParsedPrototype, linkParameters, i, !language.CaseSensitive);

							if (paramScore == -1)
								{  return 0;  }

							paramScore <<= 39 + ((9 - i) * 2);
							score |= paramScore;
							}

						// The tenth is special.  It's possible that functions may have more than ten parameters, so we go
						// through the rest of them and use the lowest score we get.

						long lastParamScore = ScoreParameter(topic.ParsedPrototype, linkParameters, 9, !language.CaseSensitive);
						int maxParameters = linkParameters.NumberOfParameters;

						if (topic.ParsedPrototype != null && topic.ParsedPrototype.NumberOfParameters > maxParameters)
							{  maxParameters = topic.ParsedPrototype.NumberOfParameters;  }

						for (int i = 10; i < maxParameters; i++)
							{
							long paramScore = ScoreParameter(topic.ParsedPrototype, linkParameters, i, !language.CaseSensitive);

							if (paramScore < lastParamScore)
								{  lastParamScore = paramScore;  }
							}

						if (lastParamScore == -1)
							{  return 0;  }

						lastParamScore <<= 39;
						score |= lastParamScore;
						}
					}
				}


			// ======== ======== ======== ======== ======== =BFFFFFF Rbbbbbbb brrrrrr=
			// Finish off the score with the topic properties.

			// B - Whether the topic has a body
			// F - How high on the list of topics that define the same symbol in the same file this is.
			// R - Whether the topic has a prototype.
			// b - The length of the body divided by 16.
			// r - The length of the prototype divided by 16.

			score |= ScoreTopic(topic);

			return score;
			}


		/* Function: ScoreInterpretation
		 * A function used by <ScoreLink()> to determine the C and S fields of the score for the passed interpretation.  Only
		 * those fields and the trailing 1 will be set in the returned score.  If the interpretation doesn't match, it will return
		 * zero.
		 */
		private long ScoreInterpretation (Topic topic, Link link, SymbolString interpretation)
			{
			// --C----- -------- -------- -SSSSSSS SSS----- -------- -------- -------1
			// C - Whether the topic and link's capitalization match if it matters to the language.
			// S - How high on the scope list the symbol match is.

			long scopeScore = ScoreScopeInterpretation(topic, link, interpretation);

			// S is always going to be higher for scopes than for using statements, so if there's a match and C is set we can
			// quit early because there's no way a using statement is going to top it.
			if (scopeScore > 0x3000000000000000)
				{  return scopeScore;  }

			long usingScore = ScoreUsingInterpretation(topic, link, interpretation);

			if (scopeScore > usingScore)
				{  return scopeScore;  }
			else
				{  return usingScore;  }
			}


		/* Function: ScoreScopeInterpretation
		 * A function used by <ScoreInterpretation()> to determine the C and S fields of the score for the passed interpretation 
		 * using only the scope.  Only those fields and the trailing 1 will be set in the returned score.  If the interpretation doesn't 
		 * match using the scope, it will return zero.
		 */
		private long ScoreScopeInterpretation (Topic topic, Link link, SymbolString interpretation)
			{
			// --C----- -------- -------- -SSSSSSS SSS----- -------- -------- -------1
			// C - Whether the topic and link's capitalization match if it matters to the language.
			// S - How high on the scope list the symbol match is.

			Language topicLanguage = EngineInstance.Languages.FromID(topic.LanguageID);
			CommentType commentType = EngineInstance.CommentTypes.FromID(topic.CommentTypeID);


			// Values of C:
			//		Natural Docs links:
			//			1 - Topic is documentation, case matches
			//			1 - Topic is documentation, case differs
			//			1 - Topic is file, case matches
			//			1 - Topic is file, case differs
			//			1 - Topic is code, topic language is case sensitive, case matches
			//			0 - Topic is code, topic language is case sensitive, case differs
			//			1 - Topic is code, topic language is case insensitive, case matches
			//			1 - Topic is code, topic language is case insensitive, case differs
			//		Type/Class Parent links:
			//			Assuming they're the same language...
			//			X - Topic is documentation, case matches
			//			X - Topic is documentation, case differs
			//			X - Topic is file, case matches
			//			X - Topic is file, case differs
			//			1 - Topic is code, language is case sensitive, case matches
			//			X - Topic is code, language is case sensitive, case differs
			//			1 - Topic is code, language is case insensitive, case matches
			//			1 - Topic is code, language is case insensitive, case differs

			bool caseFlagged;
			bool caseRequired;
			
			if (link.Type == LinkType.NaturalDocs)
				{  
				caseRequired = false;
				caseFlagged = (commentType.Flags.Code && topicLanguage.CaseSensitive);
				}
			else
				{
				if (commentType.Flags.Code == false)
					{  return 0;  }

				caseRequired = topicLanguage.CaseSensitive;  
				caseFlagged = false;
				}


			// --C----- -------- -------- -SSSSSSS SSS----- -------- -------- -------1
			// Our baseline.

			long score = 0x0000000000000001;
				
			int scopeListIndex;


			// If we match as a global symbol...

			if (string.Compare(topic.Symbol, interpretation, !caseRequired) == 0)
				{
				if (link.Context.ScopeIsGlobal)
					{  scopeListIndex = 0;  }
				else
					{
					// Conceptually, we had to walk down the entire hierachy to get to global:
					//    Scope A.B.C = A.B.C.Name, A.B.Name, A.Name, Name = Index 3
					// so the scope list index is the number of dividers in the scope plus one.

					int linkScopeIndex, linkScopeLength;
					link.Context.GetRawTextScope(out linkScopeIndex, out linkScopeLength);

					int dividers = link.Context.RawText.Count(SymbolString.SeparatorChar, linkScopeIndex, linkScopeLength);
					scopeListIndex = dividers + 1;
					}

				// --C----- -------- -------- -SSSSSSS SSS----- -------- -------- -------=
				// Apply C
				if (!caseFlagged || string.Compare(topic.Symbol, interpretation, false) == 0)
					{  score |= 0x2000000000000000;  }
				}


			// If the topic ends with the interepretation, such as "A.B.C.Name" and "Name"...

			else if (topic.Symbol.EndsWith(interpretation, !caseRequired))
				{
				string topicSymbolString = topic.Symbol.ToString();
				int topicScopeIndex = 0;
				int topicScopeLength = topicSymbolString.Length - interpretation.ToString().Length - 1;

				// See if the link's scope can completely encompass the remaining scope:
				//    Topic A.B.C.Name + Link Name + Link Scope A.B.C = yes
				//    Topic A.B.C.Name + Link Name + Link Scope A.B = no
				//    Topic A.B.C.Name + Link Name + Link Scope A.B.C.D = yes, it can walk up the hierarchy
				//    Topic A.B.C.Name + Link Name + Link Scope A.B.CC = no, can't split a word
				//    Topic A.B.C.Name + Link Name + Link Scope X.Y.Z = no

				string linkContextString = link.Context.RawText;
				int linkScopeIndex, linkScopeLength;
				link.Context.GetRawTextScope(out linkScopeIndex, out linkScopeLength);

				// If the remaining topic scope is a substring or equal to the link scope...
				if (topicScopeLength <= linkScopeLength && 
					 string.Compare(linkContextString, linkScopeIndex, topicSymbolString, topicScopeIndex, topicScopeLength, !caseRequired) == 0)
					{
					if (topicScopeLength == linkScopeLength)
						{
						// If it's an exact match, this is considered the first entry on our conceptual scope list.
						scopeListIndex = 0;
						}

					else // topicScopeLength < linkScopeLength
						{
						// If the scope was a substring, the next character needs to be a separator so we don't split a word.
						if (linkContextString[topicScopeLength] != SymbolString.SeparatorChar)
							{  return 0;  }

						// The scope list index is the number of separators we trimmed off:
						//    Link scope: A.B.C.D
						//    Remaining topic scope: A.B
						//    Scope list:
						//       0 - A.B.C.D
						//       1 - A.B.C
						//       2 - A.B
						//       3 - A
						//       4 - global
						scopeListIndex = linkContextString.Count(SymbolString.SeparatorChar, linkScopeIndex + topicScopeLength,
																								  linkScopeLength - topicScopeLength);
						}

					// --C----- -------- -------- -SSSSSSS SSS----- -------- -------- -------=
					// Apply C
					if (!caseFlagged || 
						(topicSymbolString.EndsWith(interpretation, false, System.Globalization.CultureInfo.CurrentCulture) == true &&
						 string.Compare(linkContextString, linkScopeIndex, topicSymbolString, topicScopeIndex, topicScopeLength, false) == 0) )
						{  score |= 0x2000000000000000;  }
					}
				else
					{  return 0;  }
				}
			else
				{  return 0;  }


			// --=----- -------- -------- -SSSSSSS SSS----- -------- -------- -------=
			// Encode the scope index.  We want lower indexes to have a higher score.

			if (scopeListIndex > 1023)
				{  scopeListIndex = 1023;  }

			long scopeListBits = 1023 - scopeListIndex;
			scopeListBits <<= 29;

			score |= scopeListBits;

			return score;
			}


		/* Function: ScoreUsingInterpretation
		 * A function used by <ScoreInterpretation()> to determine the C and S fields of the score for the passed interpretation 
		 * using only the using statements.  Only those fields and the trailing 1 will be set in the returned score.  If the interpretation 
		 * doesn't match using the using statements, it will return zero.
		 */
		private long ScoreUsingInterpretation (Topic topic, Link link, SymbolString interpretation)
			{
			// --C----- -------- -------- -SSSSSSS SSS----- -------- -------- -------1
			// C - Whether the topic and link's capitalization match if it matters to the language.
			// S - How high on the scope list the symbol match is.

			IList<UsingString> usingStrings = link.Context.GetUsingStatements();

			if (usingStrings == null || usingStrings.Count == 0)
				{  return 0;  }

			Language topicLanguage = EngineInstance.Languages.FromID(topic.LanguageID);
			CommentType commentType = EngineInstance.CommentTypes.FromID(topic.CommentTypeID);


			// Values of C:
			//		Natural Docs links:
			//			1 - Topic is documentation, case matches
			//			1 - Topic is documentation, case differs
			//			1 - Topic is file, case matches
			//			1 - Topic is file, case differs
			//			1 - Topic is code, topic language is case sensitive, case matches
			//			0 - Topic is code, topic language is case sensitive, case differs
			//			1 - Topic is code, topic language is case insensitive, case matches
			//			1 - Topic is code, topic language is case insensitive, case differs
			//		Type/Class Parent links:
			//			Assuming they're the same language...
			//			X - Topic is documentation, case matches
			//			X - Topic is documentation, case differs
			//			X - Topic is file, case matches
			//			X - Topic is file, case differs
			//			1 - Topic is code, language is case sensitive, case matches
			//			X - Topic is code, language is case sensitive, case differs
			//			1 - Topic is code, language is case insensitive, case matches
			//			1 - Topic is code, language is case insensitive, case differs

			bool caseFlagged;
			bool caseRequired;
			
			if (link.Type == LinkType.NaturalDocs)
				{  
				caseRequired = false;
				caseFlagged = (commentType.Flags.Code && topicLanguage.CaseSensitive);
				}
			else
				{
				if (commentType.Flags.Code == false)
					{  return 0;  }

				caseRequired = topicLanguage.CaseSensitive;  
				caseFlagged = false;
				}


			// Find the scope list index to start at, since the actual scopes come before the using statements.
			//    Scope list:
			//       0 - A.B.C.Link
			//       1 - A.B.Link
			//       2 - A.Link
			//       3 - Link
			//       4 - Link + first using statement
			// So if there's a scope, the starting index is the number of separators in the scope + 2.  Otherwise it's one.
			//    Scope list:
			//       0 - Link
			//       1 - Link + first using statement

			int scopeListIndex;

			if (link.Context.ScopeIsGlobal)
				{  scopeListIndex = 1;  }
			else
				{
				int scopeIndex, scopeLength;
				link.Context.GetRawTextScope(out scopeIndex, out scopeLength);

				scopeListIndex = link.Context.RawText.Count(SymbolString.SeparatorChar, scopeIndex, scopeLength) + 2;
				}


			// Go through each using statement looking for the best score.

			long bestScore = 0;

			foreach (var usingString in usingStrings)
				{
				SymbolString newInterpretation;
				bool newInterpretationPossible;

				if (usingString.Type == UsingString.UsingType.AddPrefix)
					{
					newInterpretation = usingString.PrefixToAdd + interpretation;
					newInterpretationPossible = true;
					}
				else if (usingString.Type == UsingString.UsingType.ReplacePrefix)
					{
					SymbolString prefixToRemove = usingString.PrefixToRemove;
					string prefixToRemoveString = prefixToRemove.ToString();
					string interpretationString = interpretation.ToString();

					if (interpretationString.Length > prefixToRemoveString.Length &&
						interpretation.StartsWith(prefixToRemove, !caseRequired))
						{
						newInterpretation = usingString.PrefixToAdd + SymbolString.FromExportedString(interpretationString.Substring(prefixToRemoveString.Length + 1));
						newInterpretationPossible = true;
						}
					else
						{  
						newInterpretation = new SymbolString();  // to make the compiler shut up
						newInterpretationPossible = false;  
						}
					}
				else
					{  throw new NotImplementedException();  }


				if (newInterpretationPossible && string.Compare(newInterpretation, topic.Symbol, !caseRequired) == 0)
					{
					// --C----- -------- -------- -SSSSSSS SSS----- -------- -------- -------1
					// Our baseline.

					long score = 0x0000000000000001;


					// --C----- -------- -------- -SSSSSSS SSS----- -------- -------- -------=
					// Encode the scope index.  We want lower indexes to have a higher score.

					if (scopeListIndex > 1023)
						{  scopeListIndex = 1023;  }

					long scopeListBits = 1023 - scopeListIndex;
					scopeListBits <<= 29;

					score |= scopeListBits;


					// --C----- -------- -------- -======= ===----- -------- -------- -------=
					// Determine C.  If C is set we can quit early because it would be impossible for a later using statement to
					// generate a higher score.

					if (!caseFlagged || string.Compare(newInterpretation, topic.Symbol, false) == 0)
						{  
						score |= 0x2000000000000000;  
						bestScore = score;
						break;
						}
					else
						{
						if (score > bestScore)
							{  bestScore = score;  }
						}
					}

				scopeListIndex++;
				}

			return bestScore;
			}


		/* Function: GetInterpretationIndex
		 * Retrieves the interpretation index from a link score.
		 */
		static public int GetInterpretationIndex (long linkScore)
			{
			// -------- -------- -------- -------- ---IIIII I------- -------- --------
			linkScore &= 0x000000001F800000;
			linkScore >>= 23;

			return 63 - (int)linkScore;
			}


		/* Function: ScoreParameter
		 * Returns a two bit value representing how well the parameters match, or -1 if they match so poorly that the link and
		 * the target shouldn't be considered a match at all.
		 */
		private long ScoreParameter (ParsedPrototype prototype, ParameterString linkParameters, int index, bool ignoreCase)
			{
			// -1 - The link has a parameter but the prototype does not.
			// 00 - The prototype has a parameter but the link does not.  This allows links on partial parameters.
			// 00 - They both have parameters but do not match at all.
			// 01 - The link doesn't have a parameter but the prototype has one with a default value set.
			// 10 - The parameters match except for qualifiers or modifiers like "unsigned".
			// 11 - The parameters match completely, by type or by name, or both don't exist.

			SimpleTokenIterator linkParamStart, linkParamEnd;
			TokenIterator prototypeParamStart, prototypeParamEnd;

			bool hasLinkParam = linkParameters.GetParameter(index, out linkParamStart, out linkParamEnd);
			bool hasPrototypeParam;
			
			if (prototype == null)
				{
				hasPrototypeParam = false;

				// To shut the compiler up.
				prototypeParamStart = new TokenIterator();
				prototypeParamEnd = new TokenIterator();
				}
			else
				{  hasPrototypeParam = prototype.GetParameter(index, out prototypeParamStart, out prototypeParamEnd);  }

			if (!hasLinkParam)
				{
				if (!hasPrototypeParam)
					{  return 3;  }
				else
					{  
					// There is a prototype parameter but not a link parameter.  This will be 0 or 1 depending on whether the 
					// prototype parameter has a default value.

					while (prototypeParamStart < prototypeParamEnd)
						{
						if (prototypeParamStart.PrototypeParsingType == PrototypeParsingType.DefaultValue)
							{  return 1;  }

						prototypeParamStart.Next();
						}

					return 0;
					}
				}

			else // hasLinkParam == true
				{
				if (hasPrototypeParam == false)
					{  return -1;  }

				// Both the link and the prototype have parameters at index.

				bool typeMatch = false;
				bool typeMismatch = false;
				bool typeModifierMismatch = false;
				bool nameMatch = false;
				bool nameMismatch = false;

				int suffixLevel = 0;

				while (prototypeParamStart < prototypeParamEnd)
					{
					var type = prototypeParamStart.PrototypeParsingType;

					// We want any mismatches that occur nested in type suffixes to be scored as a modifier mismatch.
					if (type == PrototypeParsingType.OpeningTypeSuffix)
						{  suffixLevel++;  }
					else if (type == PrototypeParsingType.ClosingTypeSuffix)
						{  suffixLevel--;  }
					else if (suffixLevel > 0)
						{  type = PrototypeParsingType.TypeSuffix;  }

					switch (type)
						{
						case PrototypeParsingType.TypeModifier:
						case PrototypeParsingType.TypeQualifier:
						case PrototypeParsingType.OpeningTypeSuffix:
						case PrototypeParsingType.ClosingTypeSuffix:
						case PrototypeParsingType.TypeSuffix:
						case PrototypeParsingType.NamePrefix_PartOfType:
						case PrototypeParsingType.NameSuffix_PartOfType: 

							if (linkParamStart < linkParamEnd && linkParamStart.MatchesToken(prototypeParamStart, ignoreCase))
								{  
								linkParamStart.Next();  
								linkParamStart.NextPastWhitespace();
								}
							else
								{  typeModifierMismatch = true;  }
							break;

						case PrototypeParsingType.Type:

							if (linkParamStart < linkParamEnd && linkParamStart.MatchesToken(prototypeParamStart, ignoreCase))
								{  
								typeMatch = true;  

								linkParamStart.Next();  
								linkParamStart.NextPastWhitespace();
								}
							else
								{  typeMismatch = true;  }
							break;

						case PrototypeParsingType.Name:

							if (linkParamStart < linkParamEnd && linkParamStart.MatchesToken(prototypeParamStart, ignoreCase))
								{  
								nameMatch = true;  

								linkParamStart.Next();  
								linkParamStart.NextPastWhitespace();
								}
							else
								{  nameMismatch = true;  }
							break;
						}

					prototypeParamStart.Next();
					prototypeParamStart.NextPastWhitespace();
					}

				if (linkParamStart < linkParamEnd)
					{  return 0;  }
				if (nameMatch && !nameMismatch)
					{  return 3;  }
				if (typeMatch && !typeMismatch)
					{
					if (!typeModifierMismatch)
						{  return 3;  }
					else
						{  return 2;  }
					}

				return 0;
				}
			}


		/* Function: ScoreTopic
		 * Generates the portions of the score which depend on the topic properties only and not how well they match a link.
		 * These are the B, F, R, b, and r components which are used for breaking ties when multiple topics would otherwise 
		 * satisfy a link equally.  This is also used in class views where there are multiple definitions of the same code element 
		 * and it must decide which one to display.  Using this function for that will make it more consistent with how links will
		 * resolve.
		 */
		public static long ScoreTopic (Topic topic)
			{
			// -------- -------- -------- -------- -------- -BFFFFFF Rbbbbbbb brrrrrr1
			// B - Whether the topic has a body
			// F - How high on the list of topics that define the same symbol in the same file this is.
			// R - Whether the topic has a prototype.
			// b - The length of the body divided by 16.
			// r - The length of the prototype divided by 16.

			long score = 0x0000000000000001;


			// -------- -------- -------- -------- -------- --FFFFFF -------- -------=
			// F - How high on the list of topics that define the same symbol in the same file this is.

			long symbolDefinitionBits = topic.SymbolDefinitionNumber;

			if (symbolDefinitionBits > 63)
				{  symbolDefinitionBits = 63;  }

			symbolDefinitionBits = 63 - symbolDefinitionBits;
			symbolDefinitionBits <<= 16;

			score |= symbolDefinitionBits;


			// -------- -------- -------- -------- -------- -B====== -bbbbbbb b------=
			// B - Whether the topic has a body
			// b - The length of the body divided by 16.
			//    0-15 = 0
			//    16-31 = 1
			//    ...
			//		4064-4079 = 254
			//		4080+ = 255

			// Use BodyLength so we can exclude Body from the query.
			if (topic.BodyLength > 0)
				{
				long bodyBits = topic.BodyLength / 16;

				if (bodyBits > 255)
					{  bodyBits = 255;  }

				bodyBits <<= 7;
				bodyBits |= 0x0000000000400000;

				score |= bodyBits;
				}


			// -------- -------- -------- -------- -------- -======= R======= =rrrrrr=
			// R - Whether the topic has a prototype.
			// r - The length of the prototype divided by 16.
			//    0-15 = 0
			//    16-31 = 1
			//    ...
			//    992-1007 = 62
			//    1008+ = 63

			if (topic.Prototype != null)
				{
				long prototypeBits = topic.Prototype.Length / 16;

				if (prototypeBits > 63)
					{  prototypeBits = 63;  }

				prototypeBits <<= 1;
				prototypeBits |= 0x0000000000008000;

				score |= prototypeBits;
				}


			return score;
			}


		/* Function: IsBetterClassDefinition
		 * If two <Topics> both have the same <ClassString>, returns whether the second one serves as a better definition 
		 * than the first.  Is safe to use with topics that don't have <Topic.DefinesClass> set.
		 */
		public bool IsBetterClassDefinition (Topic currentDefinition, Topic toTest)
			{
			#if DEBUG
			if (currentDefinition.ClassString != toTest.ClassString)
				{  throw new Exception("Tried to call IsBetterClassDefinition() on two topics with different class strings.");  }
			#endif

			if (toTest.DefinesClass == false)
				{  return false;  }
			if (currentDefinition.DefinesClass == false)
				{  return true;  }

			long currentScore = ScoreTopic(currentDefinition);
			long toTestScore = ScoreTopic(toTest);

			if (toTestScore > currentScore)
				{  return true;  }
			else if (toTestScore < currentScore)
				{  return false;  }

			// If the scores are equal, compare the paths.  Having a path that sorts higher isn't indicitive of anything, it just makes
			// sure the results of this function are consistent between runs.

			Path currentPath = EngineInstance.Files.FromID(currentDefinition.FileID).FileName;
			Path toTestPath = EngineInstance.Files.FromID(toTest.FileID).FileName;

			int compareResult = Path.Compare(currentPath, toTestPath);

			if (compareResult < 0)
				{  return false;  }
			else if (compareResult > 0)
				{  return true;  }

			// If they're in the same file, choose the one with the lower definition number.  We don't have to test >, <, and == 
			// separately because if they're still equal that means they're both the same topic and either return value is fine.

			return (toTest.FilePosition < currentDefinition.FilePosition);
			}


		/* Function: WorkOnResolvingLinks
		 * 
		 * Works on the task of resolving links due to topics changing or new links being added.  This is a parallelizable 
		 * task so multiple threads can call this function and the work will be divided up between them.
		 * 
		 * This function returns if it's cancelled or there is no more work to be done.  If there is only one thread working 
		 * on this then the task is complete, but if there are multiple threads the task isn't complete until they all have 
		 * returned.  This one may have returned because there was no more work for this thread to do, but other threads 
		 * are still working.
		 */
		public void WorkOnResolvingLinks (CancelDelegate cancelled)
			{
			using (Accessor accessor = GetAccessor())
				{
				while (!cancelled())
					{
					// We'll piggyback on the accessor's database lock to access the state variables.
					accessor.GetReadPossibleWriteLock();

					try
						{
						if (reparsingEverything)
							{
							accessor.UpgradeToReadWriteLock();

							// If we're reparsing everything, that means all links will be readded to the database as new and all of them
							// will be handled by linksToResolve.  We don't have to worry about new topics changing the definition of 
							// unchanged links so we can clear this out to lessen the workload.
							newTopicsByEndingSymbol.Clear();

							// We change this back to false afterwards so that any changes that occur after we started resolving will be 
							// treated differentially.  Once something is taken off linksToResolve we can no longer guarantee that new
							// topics won't affect anything.
							reparsingEverything = false;

							// DEPENDENCY: ResolvingUnitsOfWorkRemaining() depends on this behavior.

							// Leave the lock as read/write.
							}

						if (!linksToResolve.IsEmpty)
							{
							accessor.UpgradeToReadWriteLock();

							int linkID = linksToResolve.Highest;
							linksToResolve.Remove(linkID);

							accessor.DowngradeToReadPossibleWriteLock();

							ResolveLink(linkID, accessor);
							}

						else if (newTopicsByEndingSymbol.Count > 0)
							{
							accessor.UpgradeToReadWriteLock();

							var enumerator = newTopicsByEndingSymbol.GetEnumerator();
							enumerator.MoveNext();  // It's not positioned on the first element by default.

							EndingSymbol endingSymbol = enumerator.Current.Key;
							IDObjects.NumberSet topicIDs = enumerator.Current.Value;

							newTopicsByEndingSymbol.Remove(endingSymbol);

							accessor.DowngradeToReadPossibleWriteLock();

							ResolveNewTopics(topicIDs, endingSymbol, accessor);
							}

						else
							{  break;  }
						}

					finally
						{  accessor.ReleaseLock();  }
					}
				}
			}


		/* Function: ResolveLink
		 * 
		 * Calculates a new target for the passed link ID.
		 * 
		 * Requirements:
		 * 
		 *		- Requires at least a read/possible write lock.  If the link target changes it will be upgraded automatically.
		 *		
		 */
		protected void ResolveLink (int linkID, Accessor accessor)
			{
			Link link = accessor.GetLinkByID(linkID, Accessor.GetLinkFlags.DontLookupClasses);
			List<EndingSymbol> endingSymbols = accessor.GetAlternateLinkEndingSymbols(linkID);

			if (endingSymbols == null)
				{  endingSymbols = new List<EndingSymbol>();  }

			endingSymbols.Add(link.EndingSymbol);

			// We only need the body's length, not its contents.
			List<Topic> topics = accessor.GetTopicsByEndingSymbol(endingSymbols, Delegates.NeverCancel, 
																												 Accessor.GetTopicFlags.BodyLengthOnly |
																												 Accessor.GetTopicFlags.DontLookupClasses |
																												 Accessor.GetTopicFlags.DontLookupContexts);

			List<LinkInterpretation> alternateInterpretations = null;

			if (link.Type == LinkType.NaturalDocs)
				{
				string ignore;
				alternateInterpretations = EngineInstance.Comments.NaturalDocsParser.LinkInterpretations(link.Text, 
																												Comments.Parsers.NaturalDocs.LinkInterpretationFlags.FromOriginalText |
																												Comments.Parsers.NaturalDocs.LinkInterpretationFlags.AllowNamedLinks |
																												Comments.Parsers.NaturalDocs.LinkInterpretationFlags.AllowPluralsAndPossessives,
																												out ignore);

				}
	
			int bestMatchTopicID = 0;
			int bestMatchClassID = 0;
			long bestMatchScore = 0;

			foreach (Topic topic in topics)
				{
				long score = ScoreLink(link, topic, bestMatchScore, alternateInterpretations);

				if (score > bestMatchScore)
					{
					bestMatchTopicID = topic.TopicID;
					bestMatchClassID = topic.ClassID;
					bestMatchScore = score;
					}
				}

			if (bestMatchTopicID != link.TargetTopicID || 
				bestMatchClassID != link.TargetClassID ||
				bestMatchScore != link.TargetScore)
				{
				int oldTargetTopicID = link.TargetTopicID;
				int oldTargetClassID = link.TargetClassID;

				link.TargetTopicID = bestMatchTopicID;
				link.TargetClassID = bestMatchClassID;
				link.TargetScore = bestMatchScore;

				accessor.UpdateLinkTarget(link, oldTargetTopicID, oldTargetClassID);
				}
			}


		/* Function: ResolveNewTopics
		 * 
		 * Goes through the IDs of newly created <Topics> and sees if they serve as better targets for any existing
		 * links.
		 * 
		 * Parameters:
		 * 
		 *		topicIDs - The set of IDs to check.  Every <Topic> represented here must have the same <EndingSymbol>.
		 *		endingSymbol - The <EndingSymbol> shared by all of the topic IDs.
		 *		accessor - The <Accessor> used for the database.
		 * 
		 * Requirements:
		 * 
		 *		- Requires at least a read/possible write lock.  If the link changes it will be upgraded automatically.
		 *		
		 */
		protected void ResolveNewTopics (IDObjects.NumberSet topicIDs, EndingSymbol endingSymbol, Accessor accessor)
			{
			// We only need the body's length, not its contents.
			List<Topic> topics = accessor.GetTopicsByID(topicIDs, Delegates.NeverCancel, 
																							  Accessor.GetTopicFlags.BodyLengthOnly |
																							  Accessor.GetTopicFlags.DontLookupClasses |
																							  Accessor.GetTopicFlags.DontLookupContexts);
			List<Link> links = accessor.GetLinksByEndingSymbol(endingSymbol, Delegates.NeverCancel,
																										 Accessor.GetLinkFlags.DontLookupClasses);

			// Go through each link and see if any of the topics serve as a better target.  It's better for the links to be the outer loop 
			// because we can generate alternate interpretations only once per link.

			foreach (Link link in links)
				{
				List<LinkInterpretation> alternateInterpretations = null;

				if (link.Type == LinkType.NaturalDocs)
					{
					string ignore;
					alternateInterpretations = EngineInstance.Comments.NaturalDocsParser.LinkInterpretations(link.Text, 
																													Comments.Parsers.NaturalDocs.LinkInterpretationFlags.FromOriginalText |
																													Comments.Parsers.NaturalDocs.LinkInterpretationFlags.AllowNamedLinks |
																													Comments.Parsers.NaturalDocs.LinkInterpretationFlags.AllowPluralsAndPossessives,
																													out ignore);

					}
	
				int bestMatchTopicID = link.TargetTopicID;
				int bestMatchClassID = link.TargetClassID;
				long bestMatchScore = link.TargetScore;

				foreach (Topic topic in topics)
					{
					// No use rescoring the existing target.
					if (topic.TopicID != link.TargetTopicID)
						{
						long score = ScoreLink(link, topic, bestMatchScore, alternateInterpretations);

						if (score > bestMatchScore)
							{
							bestMatchTopicID = topic.TopicID;
							bestMatchClassID = topic.ClassID;
							bestMatchScore = score;
							}
						}
					}

				if (bestMatchTopicID != link.TargetTopicID || 
					bestMatchClassID != link.TargetClassID ||
					bestMatchScore != link.TargetScore)
					{
					int oldTargetTopicID = link.TargetTopicID;
					int oldTargetClassID = link.TargetClassID;

					link.TargetTopicID = bestMatchTopicID;
					link.TargetClassID = bestMatchClassID;
					link.TargetScore = bestMatchScore;

					accessor.UpdateLinkTarget(link, oldTargetTopicID, oldTargetClassID);
					}
				}
			}


		/* Function: ResolvingUnitsOfWorkRemaining
		 * Returns a number representing how much work is left to be done by <WorkOnResolvingLinks()>.  What tasks the 
		 * units represent can vary, so this is intended simply to allow a percentage to be calculated.
		 */
		public long ResolvingUnitsOfWorkRemaining ()
			{
			long units = 0;
			databaseLock.GetReadOnlyLock(false);

			try
				{
				units += linksToResolve.Count;

				// Only add newTopicsByEndingSymbol if we're building differentially.  This will be cleared by WorkOnResolvingLinks()
				// if not.  We don't clear it here to avoid needing anything more than a read only lock.
				if (!reparsingEverything)
					{  units += newTopicsByEndingSymbol.Count;  }

				// DEPENDENCY: This depends on WorkOnResolvingLinks() clearing the variable when reparsingEverything is set.
				}
			finally
				{  databaseLock.ReleaseReadOnlyLock(false);  }

			return units;
			}


			
		// Group: Accessor Properties
		// These properties are internal and are only meant for use by <Accessor>.
		// __________________________________________________________________________
	
		
		/* Property: DatabaseLock
		 * The <CodeDB.Lock> used to manage access to this database.  It covers properties like <UsedTopicIDs> in addition
		 * to the SQLite database itself.
		 */
		internal Lock DatabaseLock
			{
			get
				{  return databaseLock;  }
			}
			
		/* Property: UsedTopicIDs
		 * An <IDObjects.NumberSet> of all the used topic IDs in <CodeDB.Topics>.  Its use is governed by <DatabaseLock>.
		 */
		internal IDObjects.NumberSet UsedTopicIDs
			{
			get
				{  return usedTopicIDs;  }
			}
			
		/* Property: UsedLinkIDs
		 * An <IDObjects.NumberSet> of all the used link IDs in <CodeDB.Links>.  Its use is governed by <DatabaseLock>.
		 */
		internal IDObjects.NumberSet UsedLinkIDs
			{
			get
				{  return usedLinkIDs;  }
			}
			
		/* Property: UsedClassIDs
		 * An <IDObjects.NumberSet> of all the used class IDs in <CodeDB.Classes>.  Its use is governed by <DatabaseLock>.
		 */
		internal IDObjects.NumberSet UsedClassIDs
			{
			get
				{  return usedClassIDs;  }
			}

		/* Property: UsedContextIDs
		 * An <IDObjects.NumberSet> of all the used context IDs in <CodeDB.Contexts>.  Its use is governed by <DatabaseLock>.
		 */
		internal IDObjects.NumberSet UsedContextIDs
			{
			get
				{  return usedContextIDs;  }
			}

		/* Property: LinksToResolve
		 * An <IDObjects.NumberSet> of all the link IDs in <CodeDB.Links> that have changed and need to be resolved again.
		 * Note that this is not the complete set of all unresolved links; some links may have previously resolved to nothing and
		 * there may have been no changes made that could affect them.
		 */
		internal IDObjects.NumberSet LinksToResolve
			{
			get
				{  return linksToResolve;  }
			}

		/* Property: NewTopicsByEndingSymbol
		 * 
		 * Keeps track of all newly created <Topics>.  The keys are the <Symbols.EndingSymbols> the topics use, and the values
		 * are <IDObjects.NumberSets> of all the topic IDs associated with that ending symbol.  This is used for resolving links.
		 * 
		 * Rationale:
		 * 
		 *		When a new <Topic> is created, it might serve as a better definition for existing links.  We don't want to reresolve
		 *		the links as soon as the topic is created because there may be multiple topics that affect the same links and we'd 
		 *		be wasting effort.  Instead we store which topics are new and do this after parsing is complete.
		 *		
		 *		We can't store the <Topic> objects themselves because when doing a non-differential run every topic will be new and 
		 *		we'd end up storing the entire documentation structure in memory.  Instead we store the topic IDs and look up the 
		 *		<Topics> again when it's time to resolve links.
		 *		
		 *		We store them by ending symbol instead of in one NumberSet so that we can reresolve links in batches.  Topics that 
		 *		have the same ending symbol will be candidates for the same group of links, so we can query those topics and links
		 *		into memory, reresolve them all at once, and then move on to the next ending symbol.  If we stored a single NumberSet
		 *		of topic IDs we'd have to handle the topics one by one and query for each topic's links separately.
		 */
		internal SafeDictionary<Symbols.EndingSymbol, IDObjects.NumberSet> NewTopicsByEndingSymbol
			{
			get
				{  return newTopicsByEndingSymbol;  }
			}

		/* Property: ClassIDReferenceChangeCache
		 * A cache of all the reference count changes to <CodeDB.Classes>.  Its use is governed by <DatabaseLock>.
		 */
		internal ReferenceChangeCache ClassIDReferenceChangeCache
			{
			get
				{  return classIDReferenceChangeCache;  }
			}

		/* Property: ContextIDReferenceChangeCache
		 * A cache of all the reference count changes to <CodeDB.Contexts>.  Its use is governed by <DatabaseLock>.
		 */
		internal ReferenceChangeCache ContextIDReferenceChangeCache
			{
			get
				{  return contextIDReferenceChangeCache;  }
			}
			
			
			
		// Group: Accessor Functions
		// These functions are internal and are only meant for use by <Accessor>.
		// __________________________________________________________________________
			
			
		/* Function: LockChangeWatchers
		 * Gets the list of objects watching the database for changes, which requires a lock.  The list will never be null.  You can 
		 * attempt to get this lock while holding <DatabaseLock>, but never the other way around.  Release it with
		 * <ReleaseChangeWatchers()>, after which the object can no longer be used in a thread safe manner.
		 */
		internal IList<IChangeWatcher> LockChangeWatchers ()
			{
			System.Threading.Monitor.Enter(changeWatchers);
			
			// The list can only be changed by the functions directly in the module.
			return changeWatchers.AsReadOnly();
			}
			
			
		/* Function: ReleaseChangeWatchers
		 * Releases the lock on the list obtained with <LockChangeWatchers()>.
		 */
		internal void ReleaseChangeWatchers ()
			{
			System.Threading.Monitor.Exit(changeWatchers);
			}
			
			
						
		// Group: Variables
		// __________________________________________________________________________
		
		
		/* var: connection
		 */
		protected SQLite.Connection connection;
		
		/* var: databaseLock
		 */
		protected Lock databaseLock;
		
		/* var: usedTopicIDs
		 */
		protected IDObjects.NumberSet usedTopicIDs;

		/* var: usedLinkIDs
		 */
		protected IDObjects.NumberSet usedLinkIDs;

		/* var: usedClassIDs
		 */
		protected IDObjects.NumberSet usedClassIDs;

		/* var: usedContextIDs
		 */
		protected IDObjects.NumberSet usedContextIDs;

		/* var: linksToResolve
		 */
		protected IDObjects.NumberSet linksToResolve;

		/* var: newTopicsByEndingSymbol
		 */
		protected SafeDictionary<Symbols.EndingSymbol, IDObjects.NumberSet> newTopicsByEndingSymbol;

		/* var: classIDReferenceChangeCache
		 * A cache of all the reference count changes to be applied to <CodeDB.Classes>.
		 */
		protected ReferenceChangeCache classIDReferenceChangeCache;
		
		/* var: contextIDReferenceChangeCache
		 * A cache of all the reference count changes to be applied to <CodeDB.Contexts>.
		 */
		protected ReferenceChangeCache contextIDReferenceChangeCache;
		
		/* var: changeWatchers
		 * A list of objects that are watching the database for changes.  If there are none, the list will be empty
		 * rather than null.
		 */
		protected List<IChangeWatcher> changeWatchers;

		/* var: reparsingEverything
		 * If this flag is set we are reparsing the entire codebase.  Knowing this makes resolving easier.  However, we use this
		 * flag instead of checking <Config.Manager.ReparseEverything> because that is only valid on startup.  Once we start
		 * resolving this flag gets set back to false so that future changes are recorded differentially.
		 */
		protected bool reparsingEverything;
		
		}
	}