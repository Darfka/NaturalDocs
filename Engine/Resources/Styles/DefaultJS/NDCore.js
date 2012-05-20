﻿/*
	Include in output:

	This file is part of Natural Docs, which is Copyright © 2003-2012 Greg Valure.
	Natural Docs is licensed under version 3 of the GNU Affero General Public
	License (AGPL).  Refer to License.txt or www.naturaldocs.org for the
	complete details.

	This file may be distributed with documentation files generated by Natural Docs.
	Such documentation is not covered by Natural Docs' copyright and licensing,
	and may have its own copyright and distribution terms as decided by its author.

*/

"use strict";


/* Class: NDCore
	_____________________________________________________________________________

    Various helper functions to be used throughout the other scripts.
*/
var NDCore = new function ()
	{


	// Group: Selection Functions
	// ____________________________________________________________________________


	/* Function: GetElementsByClassName

		Returns an array of HTML elements matching the passed class name.  IE 8 and earlier don't have the native DOM function
		so this simulates it.

		The tag hint is used to help optimize the IE version since it uses getElementsByTagName and this will cut down the number
		of results it has to sift through.  However, you must remember that it's a hint and not a filter -- you can't rely on the results 
		only being elements of that tag type because it won't apply when using the native DOM function.
	*/
	this.GetElementsByClassName = function (baseElement, className, tagHint)
		{
		if (baseElement.getElementsByClassName)
			{  return baseElement.getElementsByClassName(className);  }
		
		if (!tagHint)
			{  tagHint = "*";  }

		var tagArray = baseElement.getElementsByTagName(tagHint);
		var matchArray = new Array();

		var tagIndex = 0;
		var matchIndex = 0;

		while (tagIndex < tagArray.length)
			{
			if (this.HasClass(tagArray[tagIndex], className))
				{
				matchArray[matchIndex] = tagArray[tagIndex];
				matchIndex++;
				}

			tagIndex++;
			}

		return matchArray;
		};



	// Group: Class Functions
	// ____________________________________________________________________________


	/* Function: HasClass
		Returns whether the passed HTML element uses the passed class.
	*/
	this.HasClass = function (element, targetClassName)
		{
		if (element.className == undefined)
			{  return false;  }

		var index = element.className.indexOf(targetClassName);

		if (index != -1)
			{
			if ( (index == 0 || element.className.charAt(index - 1) == ' ') &&
				 (index + targetClassName.length == element.className.length ||
				  element.className.charAt(index + targetClassName.length) == ' ') )
				{  return true;  }
			}

		return false;
		};


	/* Function: AddClass
		Adds a class to the passed HTML element.
	*/
	this.AddClass = function (element, newClassName)
		{
		if (element.className == undefined)
			{
			element.className = newClassName;
			return;
			}

		var index = element.className.indexOf(newClassName);

		if (index != -1)
			{
			if ( (index == 0 || element.className.charAt(index - 1) == ' ') &&
				 (index + newClassName.length == element.className.length ||
				  element.className.charAt(index + newClassName.length) == ' ') )
				{  return;  }
			}

		if (element.className.length == 0)
			{  element.className = newClassName;  }
		else
			{  element.className += " " + newClassName;  }
		};


	/* Function: RemoveClass
		Removes a class from the passed HTML element.
	*/
	this.RemoveClass = function (element, targetClassName)
		{
		if (element.className == undefined)
			{  return;  }

		var index = element.className.indexOf(targetClassName);

		while (index != -1)
			{
			if ( (index == 0 || element.className.charAt(index - 1) == ' ') &&
				 (index + targetClassName.length == element.className.length ||
				  element.className.charAt(index + targetClassName.length) == ' ') )
				{
				var newClassName = "";

				// We'll leave surrounding spaces alone.
				if (index > 0)
					{  newClassName += element.className.substr(0, index);  }
				if (index + targetClassName.length != element.className.length)
					{  newClassName += element.className.substr(index + targetClassName.length);  }

				element.className = newClassName;
				return;
				}

			index = element.className.indexOf(targetClassName, index + 1);
			}
		};



	// Group: Positioning Functions
	// ________________________________________________________________________


	/* Function: WindowClientWidth
		 A browser-agnostic way to get the window's client width.
	*/
	this.WindowClientWidth = function ()
		{
		var width = window.innerWidth;

		// Internet Explorer
		if (width === undefined)
			{  width = document.documentElement.clientWidth;  }

		return width;
		};


	/* Function: WindowClientHeight
		 A browser-agnostic way to get the window's client height.
	*/
	this.WindowClientHeight = function ()
		{
		var height = window.innerHeight;

		// Internet Explorer
		if (height === undefined)
			{  height = document.documentElement.clientHeight;  }

		return height;
		};


	/* Function: SetToAbsolutePosition
		Sets the element to the absolute position and size passed as measured in pixels.  This assumes the element is 
		positioned using fixed or absolute.  It accounts for all sizing weirdness so that the ending offsetWidth and offsetHeight
		will match what you passed regardless of any borders or padding.  If any of the coordinates are undefined it will be
		left alone.
	*/
	this.SetToAbsolutePosition = function (element, x, y, width, height)
		{
		if (x != undefined && element.offsetLeft != x)
			{  element.style.left = x + "px";  }
		if (y != undefined && element.offsetTop != y)
			{  element.style.top = y + "px";  }
			
		// We have to use the non-standard (though universally supported) offsetWidth instead of the W3C-approved scrollWidth.
		// In all browsers offsetWidth returns the full width of the element in pixels including the border.  In Firefox and Opera 
		// scrollWidth will do the same, but in IE and WebKit it's instead equivalent to clientWidth which doesn't include the border.
		if (width != undefined && element.offsetWidth != width)
			{
			// If the width isn't already specified in pixels, set it to pixels.  We can't figure out the difference between the style
			// and offset widths otherwise.  This might cause an extra resize, but only the first time.
			if (!this.pxRegex.test(element.style.width))
				{  
				element.style.width = width + "px";  

				if (element.offsetWidth != width)
					{
					var adjustment = width - element.offsetWidth;
					element.style.width = (width + adjustment) + "px";
					}
				}
			else
				{  
				var styleWidth = RegExp.$1;
				var adjustment = styleWidth - element.offsetWidth;
				element.style.width = (width + adjustment) + "px";
				}
			}

		// Copypasta for height
		if (height != undefined && element.offsetHeight != height)
			{
			if (!this.pxRegex.test(element.style.height))
				{  
				element.style.height = height + "px";  

				if (element.offsetHeight != height)
					{
					var adjustment = height - element.offsetHeight;
					element.style.height = (height + adjustment) + "px";
					}
				}
			else
				{  
				var styleHeight = RegExp.$1;
				var adjustment = styleHeight - element.offsetHeight;
				element.style.height = (height + adjustment) + "px";
				}
			}
		};



	// Group: Hash and Path Functions
	// ________________________________________________________________________


	/* Function: NormalizeHash

		Returns a normalized version of the passed hash string.

		- The leading hash symbol will be removed if present.
		- URL encoded characters will be decoded.
		- Undefined, empty strings, and empty hashes will all be converted to an empty string so they compare as equal.

	*/
	this.NormalizeHash = function (hashString)
		{
		if (hashString == undefined)
			{  return "";  }

		// IE 6 and 7 don't support hashString[0], so use substr(0,1).
		if (hashString.substr(0,1) == "#")
			{  hashString = hashString.substr(1);  }

		hashString = decodeURI(hashString);
		return hashString;
		};



	// Group: Browser Functions
	// ________________________________________________________________________


	/* Function: IsIE
		Returns whether or not you're using Internet Explorer.  If you're going to use <IEVersion()> later, you might
		want to skip this call and test its result for undefined instead.
	*/
	this.IsIE = function ()
		{
		return (navigator.userAgent.indexOf("MSIE") != -1);
		};

	/* Function: IEVersion
		Returns the major IE version as an integer, or undefined if not using IE.
	*/
	this.IEVersion = function ()
		{
		var ieIndex = navigator.userAgent.indexOf("MSIE");

		if (ieIndex == -1)
			{  return undefined;  }
		else
			{
			// parseInt() allows random crap to appear after the numbers.  It will still interpret only the leading digit
			// characters at that location and return successfully.
			return parseInt(navigator.userAgent.substr(ieIndex + 5));
			}
		};

	/* Function: AddIEClassesToBody
		If the current browser is Internet Explorer 6 through 8, add IE6, IE7, or IE8 classes to HTML.body.  We're not 
		doing a more generalized thing like Natural Docs 1.x did because it's not generally good practice and none of 
		the other browsers should be broken enough to need it anymore.
	*/
	this.AddIEClassesToBody = function ()
		{
		var ieVersion = this.IEVersion();

		if (ieVersion >= 6 && ieVersion <= 8)  // 7 covers IE8 in IE7 compatibility mode
			{  this.AddClass(document.body, "IE" + ieVersion);  }
		};



	// Group: Prototype Functions
	// ________________________________________________________________________


	/* Function: ChangePrototypeToLongForm
		Changes the passed NDPrototype element to use the long form.  The prototype *must* be in the short form.
	*/
	this.ChangePrototypeToLongForm = function (prototype)
		{
		var newPrototype = document.createElement("div");
		newPrototype.id = prototype.id;
		newPrototype.className = prototype.className;

		this.RemoveClass(newPrototype, "ShortForm");
		this.AddClass(newPrototype, "LongForm");

		var table = prototype.firstChild;
		var newTable = document.createElement("table");
		newPrototype.appendChild(newTable);

		var newRow = newTable.insertRow(-1);
		newRow.appendChild(table.rows[0].cells[0].cloneNode(true));

		newRow = newTable.insertRow(-1);
		newRow.appendChild(table.rows[0].cells[1].cloneNode(true));

		newRow = newTable.insertRow(-1);
		newRow.appendChild(table.rows[0].cells[2].cloneNode(true));

		prototype.parentNode.replaceChild(newPrototype, prototype);
		};

	
	/* Function: ChangePrototypeToShortForm
		Changes the passed NDPrototype element to use the short form.  The prototype *must* be in the long form.
	*/
	this.ChangePrototypeToShortForm = function (prototype)
		{
		var newPrototype = document.createElement("div");
		newPrototype.id = prototype.id;
		newPrototype.className = prototype.className;

		this.RemoveClass(newPrototype, "LongForm");
		this.AddClass(newPrototype, "ShortForm");

		var table = prototype.firstChild;
		var newTable = document.createElement("table");
		newPrototype.appendChild(newTable);

		var newRow = newTable.insertRow(-1);
		newRow.appendChild(table.rows[0].cells[0].cloneNode(true));
		newRow.appendChild(table.rows[1].cells[0].cloneNode(true));
		newRow.appendChild(table.rows[2].cells[0].cloneNode(true));

		prototype.parentNode.replaceChild(newPrototype, prototype);
		};



	// Group: Style Functions
	// ________________________________________________________________________


	/* Function: GetComputedStyle
		Returns the computed CSS style for the passed element in a browser-neutral way.  It first tries the element's 
		inline styles in case it overrides them, and if not, retrieves the results created from the style sheets.  Returns 
		undefined if it's not set.
	*/
	this.GetComputedStyle = function (element, style)
		{
		// First try inline.
		var result = element.style[style];

		// All tested browsers return an empty string if it's not set.
		if (result != "")
			{  return result;  }

		// Now try computed.  This was tested to work in Firefox 3.6+, Chrome 12+, and Opera 11.6.
		// IE works starting with 9 but 6-8 are out of luck.
		// Online docs say Safari only supports document.defaultView.getComputedStyle(), but Safari 5 handles this fine.
		if (window.getComputedStyle)
			{
			return window.getComputedStyle(element, "")[style];
			}

		// IE 6-8 method
		else if (element.currentStyle)
			{
			return element.currentStyle[style];
			}

		else
			{  
			return undefined;
			}
		};

	/* Function: GetComputedPixelWidth
		Similar to <GetComputedStyle()> except that it returns the property as an integer representing the pixel width.
		If the CSS property is in any format other than "#px" it will return zero, so it can't decode "#em", "#ex", etc.
	*/
	this.GetComputedPixelWidth = function (element, style)
		{
		var result = this.GetComputedStyle(element, style);

		if (this.pxRegex.test(result))
			{  return parseInt(RegExp.$1, 10);  }
		else
			{  return 0;  }
		};



	// Group: Variables
	// ________________________________________________________________________


	/* var: pxRegex
		A regular expression that can interpret "12px" styles, leaving the integer in the RegExp.$1 variable.
	*/
	this.pxRegex = /^([0-9]+)px$/i;

	};



// Section: Extension Functions
// ____________________________________________________________________________


/* Function: String.StartsWith
	Returns whether the string starts with or is equal to the passed string.
*/
String.prototype.StartsWith = function (other)
	{
	if (other === undefined)
		{  return false;  }

	return (this.length >= other.length && this.substr(0, other.length) == other);
	};


/* Function: String.EntityDecode
	Returns the string with entity chars like &amp; replaced with their original characters.  Only substitutes characters
	found in <GregValure.NaturalDocs.Engine.StringExtensions.EntityEncode()>.
*/
String.prototype.EntityDecode = function ()
	{
	// DEPENDENCY: Must update this whenever StringExtensions.EntityEncode() is changed.

	var output = this;

	// Using string constants instead of regular expressions doesn't allow a global substitution.
	output = output.replace(/&lt;/g, "<");
	output = output.replace(/&gt;/g, ">");
	output = output.replace(/&quot;/g, "\"");
	output = output.replace(/&amp;/g, "&");

	return output;
	};


/*
	Class: NDLocation
	___________________________________________________________________________

	A class encompassing all the information decoded from a Natural Docs hash path.

*/
function NDLocation (hashString)
	{

	// Group: Private Functions
	// ________________________________________________________________________


	/* Private Function: Constructor
	 */
	this.Constructor = function (hashString)
		{
		this.hashString = NDCore.NormalizeHash(hashString);

		if (this.hashString.match(/^File[0-9]*:/) != null)
			{
			this.type = "File";

			// The first colon after File:, which will always exist if we're a file hash path.
			var pathSeparator = this.hashString.indexOf(':', 4);

			// The first colon after the path, which may or may not exist.
			var memberSeparator = this.hashString.indexOf(':', pathSeparator + 1);

			if (memberSeparator == -1)
				{
				this.path = this.hashString;
				}
			else
				{
				this.path = this.hashString.substr(0, memberSeparator);
				this.member = this.hashString.substr(memberSeparator + 1);

				if (this.member == "")
					{  this.member = undefined;  }
				}

			this.AddFileURLs();
			}
		else
			{
			// All empty and invalid hashes show the home page.
			this.type = "Home";
			this.AddHomeURLs();
			}
		};


	/* Private Function: AddHomeURLs
		Adds the contentPage property to the location object.  The object's type must be "Home".
	*/
	this.AddHomeURLs = function ()
		{
		this.contentPage = "other/home.html";
		};

	
	/* Private Function: AddFileURLs
		Adds the contentPage, summaryFile, and summaryTTFile properties to the location object.  The object's type
		must be "File".
	*/
	this.AddFileURLs = function ()
		{
		var pathPrefix = this.path.match(/^File([0-9]*):/);
		var basePath = "files" + pathPrefix[1] + "/" + this.path.substr(pathPrefix[0].length);

		var lastSeparator = basePath.lastIndexOf('/');
		var filename = basePath.substr(lastSeparator + 1);
		filename = filename.replace(/\./g, '-');
		
		basePath = basePath.substr(0, lastSeparator + 1) + filename;

		this.contentPage = basePath + ".html";
		this.summaryFile = basePath + "-Summary.js";
		this.summaryTTFile = basePath + "-SummaryToolTips.js";

		if (this.member != undefined)
			{  this.contentPage += '#' + this.member;  }
		};



	// Group: Universal Variables
	// These variables will always be present.
	// ___________________________________________________________________________


	/*
		var: type
		A string representing the type of location it is, such as "Home" or "File".  Code should be able to
		handle unknown strings as the types may be expanded in the future.

		var: hashString
		The full normalized hash string.

		var: contentPage
		The URL to the content page.
	*/



	// Group: File Hash Variables
	// These variables will be present if <type> is set to "File".
	// ___________________________________________________________________________


	/*
		var: path
		The path to the source file, such as "File:Folder/Folder/Source.cs".

		var: member
		The member of the file, such as "Class.Class.Member" in "File:Folder/Folder/Source.cs:Class.Class.Member".
		This will be undefined if one was not specified in the hash path.

		var: summaryFile
		The URL to the summary data file.

		var: summaryTTFile
		The URL to the summary tooltips data file.
	*/


	// Call the constructor now that all the members are prepared.
	this.Constructor(hashString);

	};