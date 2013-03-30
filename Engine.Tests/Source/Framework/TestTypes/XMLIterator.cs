﻿/* 
 * Class: GregValure.NaturalDocs.Engine.Tests.Framework.TestTypes.XMLIterator
 * ____________________________________________________________________________
 * 
 * A class to test <Engine.Comments.Parsers.XMLIterator's> ability to parse XML.
 * 
 * Commands:
 * 
 *		> // text
 *		Comment.  Ignored.
 *		
 *		> Property [name] in <tag>
 *		Attempts to retrieve the named property in the tag.
 *		
 *		> content
 *		Parses the content.
 * 
 */

// This file is part of Natural Docs, which is Copyright © 2003-2012 Greg Valure.
// Natural Docs is licensed under version 3 of the GNU Affero General Public License (AGPL)
// Refer to License.txt for the complete details


using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using GregValure.NaturalDocs.Engine;
using GregValure.NaturalDocs.Engine.Comments.Parsers;
using GregValure.NaturalDocs.Engine.Tests.Framework;
using GregValure.NaturalDocs.Engine.Tokenization;


namespace GregValure.NaturalDocs.Engine.Tests.Framework.TestTypes
	{
	public class XMLIterator : Framework.TextCommands
		{

		public override string OutputOf (IList<string> commands)
			{
			StringBuilder output = new StringBuilder();

			foreach (string command in commands)
				{
				output.AppendLine(command);
				Match findPropertyMatch = FindPropertyRegex.Match(command);
				
				try
					{
					if (command == "" || command.StartsWith("//"))
						{
						// Ignore
						}
					else if (findPropertyMatch.Success)
						{
						string propertyName = findPropertyMatch.Groups[1].ToString();
						Tokenizer xmlTag = new Tokenizer( findPropertyMatch.Groups[2].ToString() );

						Engine.Comments.Parsers.XMLIterator iterator = new Engine.Comments.Parsers.XMLIterator(xmlTag.FirstToken, xmlTag.LastToken);

						if (iterator.Type != XMLElementType.Tag)
							{  output.AppendLine("- Not an XML tag");  }
						else
							{  
							string value = iterator.TagProperty(propertyName);

							if (value != null)
								{  output.AppendLine("- " + value);  }
							else
								{  output.AppendLine("- Not found");  }
							}
						}
					else
						{
						Tokenizer xml = new Tokenizer(command.Trim());
						Engine.Comments.Parsers.XMLIterator iterator = new Engine.Comments.Parsers.XMLIterator(xml.FirstToken, xml.LastToken);

						while (iterator.IsInBounds)
							{
							if (iterator.Type == XMLElementType.Tag)
								{
								output.AppendLine("- " + iterator.TagForm.ToString() + " Tag: " + iterator.TagType);

								var properties = iterator.GetAllTagProperties();

								foreach (var property in properties)
									{  output.AppendLine("  Property " + property.Key + ": " + property.Value);  }
								}
							else if (iterator.Type == XMLElementType.EntityChar)
								{
								output.AppendLine("- Entity Char: " + iterator.EntityValue);
								}
							else if (iterator.Type == XMLElementType.Text)
								{
								output.AppendLine("- Text: " + iterator.String);
								}
							else
								{
								output.AppendLine("- Unexpected element type " + iterator.Type.ToString());
								}

							iterator.Next();
							}

						output.AppendLine();
						}
					}
				catch (Exception e)
					{
					output.AppendLine("Exception: " + e.Message);
					output.AppendLine("(" + e.GetType().ToString() + ")");
					}
				}

			return output.ToString();
			}

		static private Regex FindPropertyRegex = new Regex(" *Property ([a-z\\-_]+) in (.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		}
	}