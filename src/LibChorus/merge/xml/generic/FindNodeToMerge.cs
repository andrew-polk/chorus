using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using Chorus.merge.xml.generic.xmldiff;


namespace Chorus.merge.xml.generic
{
	public interface IFindNodeToMerge
	{
		/// <summary>
		/// Should return null if parentToSearchIn is null
		/// </summary>
		XmlNode GetNodeToMerge(XmlNode nodeToMatch, XmlNode parentToSearchIn);
	}

	/// <summary>
	/// Defines a secondary algorithm for finding less ideal matches, e.g., we allow an empty
	/// node to match a non-empty (but no duplicates).
	/// </summary>
	public interface IFindPossibleNodeToMerge
	{
		XmlNode GetPossibleNodeToMerge(XmlNode nodeToMatch, List<XmlNode> possibleMatches);
	}

	public class FindByKeyAttribute : IFindNodeToMerge
	{
		private string _keyAttribute;

		public FindByKeyAttribute(string keyAttribute)
		{
			_keyAttribute = keyAttribute;
		}


		public XmlNode GetNodeToMerge(XmlNode nodeToMatch, XmlNode parentToSearchIn)
		{
			if (parentToSearchIn == null)
				return null;

			string key = XmlUtilities.GetOptionalAttributeString(nodeToMatch, _keyAttribute);
			if (string.IsNullOrEmpty(key))
			{
				return null;
			}
			// I (CP) changed this to use double quotes to allow attributes to contain single quotes.
			// My understanding is that double quotes are illegal inside attributes so this should be fine.
			// See: http://jira.palaso.org/issues/browse/WS-33895
			//string xpath = string.Format("{0}[@{1}='{2}']", nodeToMatch.Name, _keyAttribute, key);
			string xpath = string.Format("{0}[@{1}=\"{2}\"]", nodeToMatch.Name, _keyAttribute, key);

			return parentToSearchIn.SelectSingleNode(xpath);
		}

	}

	/// <summary>
	/// Search for a matching elment where multiple attributes combine
	/// to make a single "key" to identify a matching elment.
	/// </summary>
	public class FindByMultipleKeyAttributes : IFindNodeToMerge
	{
		private readonly List<string> _keyAttributes;

		public FindByMultipleKeyAttributes(List<string> keyAttributes)
		{
			_keyAttributes = keyAttributes;
		}

		public XmlNode GetNodeToMerge(XmlNode nodeToMatch, XmlNode parentToSearchIn)
		{
			if (parentToSearchIn == null)
				return null;

			var bldr = new StringBuilder(nodeToMatch.Name + "[");
			for (var i = 0; i < _keyAttributes.Count; ++i )
			{
				if (i > 0)
					bldr.Append(" and ");
				var currentAttrName = _keyAttributes[i];
				bldr.AppendFormat("@{0}='{1}'", currentAttrName, XmlUtilities.GetStringAttribute(nodeToMatch, currentAttrName));
			}
			bldr.Append("]");

			return parentToSearchIn.SelectSingleNode(bldr.ToString());
		}
	}

	/// <summary>
	/// e.g. <grammatical-info>  there can only be one
	/// </summary>
	public class FindFirstElementWithSameName : IFindNodeToMerge
	{
		public XmlNode GetNodeToMerge(XmlNode nodeToMatch, XmlNode parentToSearchIn)
		{
			if (parentToSearchIn == null)
				return null;

			return parentToSearchIn.SelectSingleNode(nodeToMatch.Name);
		}
	}

	public class FindByEqualityOfTree : IFindNodeToMerge, IFindPossibleNodeToMerge
	{
		public XmlNode GetNodeToMerge(XmlNode nodeToMatch, XmlNode parentToSearchIn)
		{
			if (parentToSearchIn == null)
				return null;

			//match any exact xml matches, including all the children

			foreach (XmlNode node in parentToSearchIn.ChildNodes)
			{
				if(nodeToMatch.Name != node.Name)
				{
					continue; // can't be equal if they don't even have the same name
				}

				if (node.GetType() == typeof(XmlText))
				{
					throw new ApplicationException("Please report: regression in FindByEqualityOfTree where the node is simply a text.");
				}
				XmlDiff d = new XmlDiff(nodeToMatch.OuterXml, node.OuterXml);
				DiffResult result = d.Compare();
				if (result == null || result.Equal)
				{
					return node;
				}
			}
			return null;
		}

		#region IFindPossibleNodeToMerge Members

		/// <summary>
		/// When looking for an exact match, we allow the fall-back of matching an empty
		/// node against one with content.
		/// </summary>
		/// <param name="nodeToMatch"></param>
		/// <param name="possibleMatches"></param>
		/// <returns></returns>
		public XmlNode GetPossibleNodeToMerge(XmlNode nodeToMatch, List<XmlNode> possibleMatches)
		{
			if (nodeToMatch.ChildNodes.Count == 0 && nodeToMatch.Attributes.Count == 0)
			{
				foreach (XmlNode possible in possibleMatches)
				{
					if (possible.Name == nodeToMatch.Name)
						return possible;
				}
			}
			else
			{
				foreach (XmlNode possible in possibleMatches)
				{
					if (possible.Name == nodeToMatch.Name && possible.ChildNodes.Count == 0
						&& possible.Attributes.Count == 0)
					{
						return possible;
					}
				}
			}
			return null;
		}

		#endregion
	}

	public class FindTextDumb : IFindNodeToMerge
	{
		//todo: this won't cope with multiple text child nodes in the same element

		public XmlNode GetNodeToMerge(XmlNode nodeToMatch, XmlNode parentToSearchIn)
		{
			if (parentToSearchIn == null)
				return null;

			//just match first text we find

			foreach (XmlNode node in parentToSearchIn.ChildNodes)
			{
				if(node.NodeType == XmlNodeType.Text)
					return node;
			}
			return null;
		}
	}
}