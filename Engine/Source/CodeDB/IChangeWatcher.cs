﻿/* 
 * Interface: CodeClear.NaturalDocs.Engine.CodeDB.IChangeWatcher
 * ____________________________________________________________________________
 * 
 * An interface for any class that wants to watch for changes in the code database.
 * 
 * Rationale:
 * 
 *		Why use lists of IChangeWatchers instead of events?  Mainly because it allows <CodeDB.Manager> to control
 *		the calling order so you can have priority watchers that get called before normal ones.
 * 
 * 
 * Multithreading: Thread Safety Notes
 * 
 *		This interface is used for receiving notifications when the database has changed.  As such, these functions can
 *		be called from any possible thread.  Make sure any structures you interact with are thread safe.
 *		
 */

// This file is part of Natural Docs, which is Copyright © 2003-2017 Code Clear LLC.
// Natural Docs is licensed under version 3 of the GNU Affero General Public License (AGPL)
// Refer to License.txt for the complete details


using System;
using CodeClear.NaturalDocs.Engine.Links;
using CodeClear.NaturalDocs.Engine.Topics;


namespace CodeClear.NaturalDocs.Engine.CodeDB
	{
	public interface IChangeWatcher
		{
		
		/* Function: OnAddTopic
		 * Called after a topic is added to the database.
		 */
		void OnAddTopic (Topic topic, EventAccessor eventAccessor);
		
		/* Function: OnUpdateTopic
		 * Called after a topic has been updated in the database.
		 */
		void OnUpdateTopic (Topic oldTopic, Topic newTopic, Topic.ChangeFlags changeFlags, EventAccessor eventAccessor);
		
		/* Function: OnDeleteTopic
		 * Called before a topic is deleted from the database.
		 */
		void OnDeleteTopic (Topic topic, EventAccessor eventAccessor);
		
		/* Function: OnAddLink
		 * Called after a link is added to the database.
		 */
		void OnAddLink (Link link, EventAccessor eventAccessor);
		
		/* Function: OnChangeLinkTarget
		 * Called after a link's target has been changed in the database.  Note that this will also be called for new links, as they
		 * are added to the database as unresolved during the parsing stage, and then changed to their targets during the
		 * resolving stage.
		 */
		void OnChangeLinkTarget (Link link, int oldTargetTopicID, int oldTargetClassID, EventAccessor eventAccessor);
		
		/* Function: OnDeleteLink
		 * Called before a link is deleted from the database.
		 */
		void OnDeleteLink (Link link, EventAccessor eventAccessor);
		
		}
	}