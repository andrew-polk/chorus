using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using Chorus.FileTypeHanders.xml;
using Chorus.merge;
using Chorus.merge.xml.generic;

namespace Chorus.FileTypeHanders.FieldWorks
{
	public class FieldWorksMerger
	{
		private readonly MergeOrder m_mergeOrder;
		private readonly IMergeStrategy m_mergeStategy;
		private readonly string m_winnerId;
		private string m_winnerXml;
		private string m_loserXml;
		private string m_commonAncestorXml;

		public FieldWorksMerger(MergeOrder mergeOrder, IMergeStrategy mergeStategy, string pathToWinner, string pathToLoser, string pathToCommonAncestor, string winnerId)
		{
			m_mergeOrder = mergeOrder;
			m_mergeStategy = mergeStategy;
			m_winnerId = winnerId;

			m_winnerXml = File.ReadAllText(pathToWinner);
			m_loserXml = File.ReadAllText(pathToLoser);
			m_commonAncestorXml = File.ReadAllText(pathToCommonAncestor);
		}

		/// <summary>Used by tests, which prefer to give us raw contents rather than paths</summary>
		public FieldWorksMerger(string winnerXml, string loserXml, string commonAncestorXml)
		{
			m_winnerXml = winnerXml;
			m_loserXml = loserXml;
			m_commonAncestorXml = commonAncestorXml;
		}

		private void Do2WayDiff(string parentXml, ref string childXml,
			Dictionary<string, XmlNode> goners, Dictionary<string, ChangedElement> dirtballs, Dictionary<string, XmlNode> newbies)
		{
			var winnerCommonListener = new ChangeAndConflictAccumulator();
			var winnerDiffer = Xml2WayDiffer.CreateFromStrings(
				parentXml,
				childXml,
				winnerCommonListener,
				"rt",
				"languageproject",
				"guid");
			childXml = null;
			try
			{
				winnerDiffer.ReportDifferencesToListener();
				foreach (var winnerDif in winnerCommonListener.Changes)
				{
					if (!(winnerDif is IXmlChangeReport))
						continue; // It could be ErrorDeterminingChangeReport, so what to do with it?

					var asXmlReport = (IXmlChangeReport)winnerDif;
					switch (winnerDif.GetType().Name)
					{
						case "XmlDeletionChangeReport":
							var gonerNode = asXmlReport.ParentNode;
							goners.Add(gonerNode.Attributes["guid"].Value, gonerNode);
							break;
						case "XmlChangedRecordReport":
							var originalNode = asXmlReport.ParentNode;
							var updatedNode = asXmlReport.ChildNode;
							dirtballs.Add(originalNode.Attributes["guid"].Value, new ChangedElement
							{
								m_parentNode = originalNode,
								m_childNode = updatedNode
							});
							break;
						case "XmlAdditionChangeReport":
							var newbieNode = asXmlReport.ChildNode;
							newbies.Add(newbieNode.Attributes["guid"].Value, newbieNode);
							break;
					}
				}
			}
			// ReSharper disable EmptyGeneralCatchClause
			catch { }
			// ReSharper restore EmptyGeneralCatchClause
		}

		public void DoMerge(string outputPathname)
		{
			// Do diff between winner and common
			var winnerGoners = new Dictionary<string, XmlNode>();
			var winnerDirtballs = new Dictionary<string, ChangedElement>();
			var newbies = new Dictionary<string, XmlNode>();
			Do2WayDiff(m_commonAncestorXml, ref m_winnerXml, winnerGoners, winnerDirtballs, newbies);

			// Do diff between loser and common
			var loserGoners = new Dictionary<string, XmlNode>();
			var loserDirtballs = new Dictionary<string, ChangedElement>();
			Do2WayDiff(m_commonAncestorXml, ref m_loserXml, loserGoners, loserDirtballs, newbies);

			// At this point we have two sets of diffs, but we need to merge them.
			// Newbies from both get added.
			// A conflict has 'winner' stay, but with a report.
			using (var writer = XmlWriter.Create(outputPathname, new XmlWriterSettings
			{
				OmitXmlDeclaration = false,
				CheckCharacters = true,
				ConformanceLevel = ConformanceLevel.Document,
				Encoding = new UTF8Encoding(false),
				Indent = true,
				IndentChars = (""),
				NewLineOnAttributes = false
			}))
			{
				// Need a reader on 'm_commonAncestorXml', much as is done for FW, but sans thread.
				// Blend in newbies, goners, and dirtballs to 'outputPathname' as in FW.
				var readerSettings = new XmlReaderSettings
						{
							CheckCharacters = false,
							ConformanceLevel = ConformanceLevel.Document,
							ProhibitDtd = true,
							ValidationType = ValidationType.None,
							CloseInput = true,
							IgnoreWhitespace = true
						};
				var enc = Encoding.UTF8;
				using (var reader = XmlReader.Create(new MemoryStream(enc.GetBytes(m_commonAncestorXml)), readerSettings))
				{
					m_commonAncestorXml = null;
					reader.MoveToContent();

					writer.WriteStartElement("languageproject");
					reader.MoveToAttribute("version");
					writer.WriteAttributeString("version", reader.Value);
					reader.MoveToElement();

					// Deal with optional custom field declarations.
					if (reader.LocalName == "AdditionalFields")
					{
						writer.WriteNode(reader, false);
					}

					// Deal with <rt> elements
					var keepReading = reader.Read();
					while (keepReading)
					{
						if (reader.EOF)
							break;

						// 'rt' node is current node in reader.
						// Fetch Guid from 'rt' node and see if it is in either
						// of the modified/deleted dictionaries.
						var transferUntouched = true;
						var currentGuid = reader.GetAttribute("guid");

						if (winnerGoners.ContainsKey(currentGuid))
						{
							transferUntouched = false;
							if (loserGoners.ContainsKey(currentGuid))
							{
								// Both deleted it.
								loserGoners.Remove(currentGuid);
							}
							else
							{
								if (loserDirtballs.ContainsKey(currentGuid))
								{
									var dirtball = loserDirtballs[currentGuid];
									// Winner deleted it, but loser edited it.
									// Make a conflict report.
									EventListener.ConflictOccurred(new RemovedVsEditedElementConflict(
										"rt",
										winnerGoners[currentGuid],
										dirtball.m_childNode,
										dirtball.m_parentNode,
										m_mergeOrder.MergeSituation,
										new ElementStrategy(false),
										m_winnerId));
									loserDirtballs.Remove(currentGuid);
								}
								// else // Winner deleted it and loser did nothing with it.
							}
							winnerGoners.Remove(currentGuid);
						}

						if (winnerDirtballs.ContainsKey(currentGuid))
						{
							transferUntouched = false;
							if (loserGoners.ContainsKey(currentGuid))
							{
								// Winner edited it, but loser deleted it.
								// Make a conflict report.
								var dirtballElement = winnerDirtballs[currentGuid];
								EventListener.ConflictOccurred(new EditedVsRemovedElementConflict(
									"rt",
									dirtballElement.m_childNode,
									loserGoners[currentGuid],
									dirtballElement.m_parentNode,
									m_mergeOrder.MergeSituation,
									new ElementStrategy(false),
									m_winnerId));

								ReplaceCurrentNode(writer, dirtballElement.m_childNode);
								winnerDirtballs.Remove(currentGuid);
								loserGoners.Remove(currentGuid);
							}
							else
							{
								if (loserDirtballs.ContainsKey(currentGuid))
								{
									// Both edited it. Check it out.
									var mergedResult = winnerDirtballs[currentGuid].m_childNode.OuterXml;
									if (!XmlUtilities.AreXmlElementsEqual(winnerDirtballs[currentGuid].m_childNode, loserDirtballs[currentGuid].m_childNode))
									{
										var dirtballElement = winnerDirtballs[currentGuid];
										mergedResult = m_mergeStategy.MakeMergedEntry(EventListener, dirtballElement.m_childNode,
																	   loserDirtballs[currentGuid].m_childNode, dirtballElement.m_parentNode);
									}
									ReplaceCurrentNode(writer, mergedResult);
									loserDirtballs.Remove(currentGuid);
								}
								else
								{
									// Winner edited it. Loser did nothing with it.
									ReplaceCurrentNode(writer, winnerDirtballs[currentGuid].m_childNode);
									winnerDirtballs.Remove(currentGuid);
								}
							}
						}

						if (loserGoners.ContainsKey(currentGuid))
						{
							// Loser deleted it but winner did nothing to it.
							// If winner had either deleted or edited it,
							// then the code above here would have been involved,
							// and currentGuid would have been removed from loserGoners.
							transferUntouched = false;
							loserGoners.Remove(currentGuid);
						}
						if (loserDirtballs.ContainsKey(currentGuid))
						{
							// Loser changed it, but winner did nothing to it.
							transferUntouched = false;
							ReplaceCurrentNode(writer, loserDirtballs[currentGuid].m_childNode);
							loserDirtballs.Remove(currentGuid);
						}

						if (!transferUntouched)
						{
							// Read to next <rt> element
							reader.ReadOuterXml();
							keepReading = reader.IsStartElement();
							continue;
						}

						// Nobody did anything with the currentsource node, so just copy it to output.
						writer.WriteNode(reader, false);
						keepReading = reader.IsStartElement();
					}

					// Write out all new kids from winner and loser.
					readerSettings = new XmlReaderSettings
					{
						CheckCharacters = false,
						ConformanceLevel = ConformanceLevel.Fragment,
						ProhibitDtd = true,
						ValidationType = ValidationType.None,
						CloseInput = true,
						IgnoreWhitespace = true
					};
					foreach (var newby in newbies.Values)
					{
						// Note: If we need to put in a XmlAdditionChangeReport for newbies from 'loser',
						// Note: then we will need two 'newbie' lists.
						using (var nodeReader = XmlReader.Create(new MemoryStream(enc.GetBytes(newby.OuterXml)), readerSettings))
						{
							writer.WriteNode(nodeReader, false);
						}
					}

					writer.WriteEndElement();
				}
			}
		}

		private static void ReplaceCurrentNode(XmlWriter writer, XmlNode replacementNode)
		{
			ReplaceCurrentNode(writer, replacementNode.OuterXml);
		}

		private static void ReplaceCurrentNode(XmlWriter writer, string replacementValue)
		{
			using (var tempReader = XmlReader.Create(
				new MemoryStream(Encoding.UTF8.GetBytes(replacementValue)),
				new XmlReaderSettings
				{
					CheckCharacters = false,
					ConformanceLevel = ConformanceLevel.Fragment,
					ProhibitDtd = true,
					ValidationType = ValidationType.None,
					CloseInput = true,
					IgnoreWhitespace = true
				}))
			{
				writer.WriteNode(tempReader, false);
			}
		}

		public IMergeEventListener EventListener
		{ get; set; }

		private class ChangedElement
		{
			internal XmlNode m_parentNode;
			internal XmlNode m_childNode;
		}
	}
}