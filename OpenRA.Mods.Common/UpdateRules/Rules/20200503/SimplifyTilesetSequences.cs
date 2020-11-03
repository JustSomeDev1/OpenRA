#region Copyright & License Information
/*
 * Copyright 2007-2020 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Collections.Generic;
using System.Reflection;

namespace OpenRA.Mods.Common.UpdateRules.Rules
{
	public class SimplifyTilesetSequences : UpdateRule
	{
		public override string Name { get { return "Replace mod-specific tileset sequence properties with a tileset: image list."; } }
		public override string Description
		{
			get
			{
				return "Sequence artwork filenames are no longer automatically inferred.\n" +
					"and the AddExtension, UseTilesetExtension, UseTilesetNodes, and TilesetOverrides\n" +
					"fields have been removed.\n\n" +
					"Standard images can be defined as before, but must now include the filename extension\n" +
					"or you may define an Image node, which accepts key-value child nodes to specify\n" +
					"tileset-specific images.";
			}
		}

		string defaultSpriteExtension = ".shp";
		Dictionary<string, string> tilesetExtensions = new Dictionary<string, string>();
		Dictionary<string, string> tilesetCodes = new Dictionary<string, string>();
		bool enabled;
		bool foundInherits;
		bool reportedModYamlChanges;

		public override IEnumerable<string> BeforeUpdate(ModData modData)
		{
			// Don't reload data when processing maps
			if (enabled)
				yield break;

			// HACK: We need to read the obsolete yaml definitions to be able to update the sequences
			// TilesetSpecificSpriteSequence no longer defines fields for these, so we must take them directly from mod.yaml
			// This is the least hacky way to reliably read it(!)
			var yamlField = modData.Manifest.GetType().GetField("yaml", BindingFlags.Instance | BindingFlags.NonPublic);
			var yaml = (Dictionary<string, MiniYaml>)yamlField?.GetValue(modData.Manifest);

			if (yaml != null && yaml.TryGetValue("SpriteSequenceFormat", out var spriteSequenceFormatYaml))
			{
				if (spriteSequenceFormatYaml.Value != "TilesetSpecificSpriteSequence" && spriteSequenceFormatYaml.Value != "ClassicTilesetSpecificSpriteSequence")
					yield break;

				var spriteSequenceFormatNode = new MiniYamlNode("", spriteSequenceFormatYaml);
				var defaultSpriteExtensionNode = spriteSequenceFormatNode.LastChildMatching("DefaultSpriteExtension");
				if (defaultSpriteExtensionNode != null)
					defaultSpriteExtension = defaultSpriteExtensionNode.Value.Value;

				var tilesetExtensionsNode = spriteSequenceFormatNode.LastChildMatching("TilesetExtensions");
				if (tilesetExtensionsNode != null)
					foreach (var n in tilesetExtensionsNode.Value.Nodes)
						tilesetExtensions[n.Key] = n.Value.Value;

				var tilesetCodesNode = spriteSequenceFormatNode.LastChildMatching("TilesetCodes");
				if (tilesetCodesNode != null)
					foreach (var n in tilesetCodesNode.Value.Nodes)
						tilesetCodes[n.Key] = n.Value.Value;

				enabled = true;
			}
		}

		public override IEnumerable<string> AfterUpdate(ModData modData)
		{
			if (reportedModYamlChanges || !enabled)
				yield break;

			yield return "The DefaultSpriteExtension, TilesetExtensions, and TilesetCodes fields defined\n" +
				"under SpriteSequenceFormat in your mod.yaml are no longer used, and can be removed.";

			if (foundInherits)
				yield return "At least one of your sequences definitions is inherited from another definition,\n" +
					"so it is likely that this rule has not correctly updated all of your definitions.\n\n" +
					"Run the --check-missing-sprites utility command to identify errors and fix them manually.";

			reportedModYamlChanges = true;
		}

		public override IEnumerable<string> UpdateSequenceNode(ModData modData, MiniYamlNode sequenceNode)
		{
			if (!enabled)
				yield break;

			var defaultImage = sequenceNode.Key;
			var defaultAddExtension = true;
			var defaultUseTilesetExtension = false;
			var defaultUseTilesetCode = false;
			var defaultTilesetOverrides = new Dictionary<string, string>();
			var explicitDefaultImage = false;
			foreach (var defaults in sequenceNode.ChildrenMatching("Defaults"))
			{
				if (!string.IsNullOrEmpty(defaults.Value.Value))
					explicitDefaultImage = true;

				defaultImage = defaults.Value.Value ?? defaultImage;
				var addExtensionNode = defaults.LastChildMatching("AddExtension");
				if (addExtensionNode != null)
				{
					defaultAddExtension = FieldLoader.GetValue<bool>("AddExtension", addExtensionNode.Value.Value);
					defaults.RemoveNode(addExtensionNode);
				}

				var useTilesetExtensionNode = defaults.LastChildMatching("UseTilesetExtension");
				if (useTilesetExtensionNode != null)
				{
					defaultUseTilesetExtension = FieldLoader.GetValue<bool>("UseTilesetExtension", useTilesetExtensionNode.Value.Value);
					defaults.RemoveNode(useTilesetExtensionNode);
				}

				var useTilesetCodeNode = defaults.LastChildMatching("UseTilesetCode");
				if (useTilesetCodeNode != null)
				{
					defaultUseTilesetCode = FieldLoader.GetValue<bool>("UseTilesetCode", useTilesetCodeNode.Value.Value);
					defaults.RemoveNode(useTilesetCodeNode);
				}

				var tilesetOverridesNode = defaults.LastChildMatching("TilesetOverrides");
				if (tilesetOverridesNode != null)
				{
					foreach (var n in tilesetOverridesNode.Value.Nodes)
						defaultTilesetOverrides[n.Key] = n.Value.Value;

					defaults.RemoveNode(tilesetOverridesNode);
				}

				if (defaultAddExtension && !string.IsNullOrEmpty(defaults.Value.Value))
					defaults.ReplaceValue(defaults.Value.Value + defaultSpriteExtension);
			}

			foreach (var sequence in sequenceNode.Value.Nodes)
				ProcessNode(modData, sequence, defaultImage, explicitDefaultImage, defaultAddExtension, defaultUseTilesetExtension, defaultUseTilesetCode, defaultTilesetOverrides);
		}

		void ProcessNode(ModData modData, MiniYamlNode sequence, string defaultImage, bool explicitDefaultImage,
			bool defaultAddExtension, bool defaultUseTilesetExtension, bool defaultUseTilesetCode,
			Dictionary<string, string> defaultTilesetOverrides)
		{
			if (sequence.Key == "Inherits")
				foundInherits = true;

			if (sequence.Key == "Defaults" || sequence.Key == "Inherits" || string.IsNullOrEmpty(sequence.Key))
				return;

			var combineNode = sequence.LastChildMatching("Combine");
			if (combineNode != null)
			{
				var i = 0;
				foreach (var combine in combineNode.Value.Nodes)
				{
					ProcessNode(modData, combine, combine.Key ?? defaultImage, explicitDefaultImage, defaultAddExtension, defaultUseTilesetExtension, defaultUseTilesetCode, defaultTilesetOverrides);
					combine.Key = (i++).ToString();
				}

				return;
			}

			var sequenceImage = sequence.Value.Value ?? defaultImage;
			var addExtension = defaultAddExtension;
			var addExtensionNode = sequence.LastChildMatching("AddExtension");
			if (addExtensionNode != null)
			{
				addExtension = FieldLoader.GetValue<bool>("AddExtension", addExtensionNode.Value.Value);
				sequence.RemoveNode(addExtensionNode);
			}

			var useTilesetExtension = defaultUseTilesetExtension;
			var useTilesetExtensionNode = sequence.LastChildMatching("UseTilesetExtension");
			if (useTilesetExtensionNode != null)
			{
				useTilesetExtension = FieldLoader.GetValue<bool>("UseTilesetExtension", useTilesetExtensionNode.Value.Value);
				sequence.RemoveNode(useTilesetExtensionNode);
			}

			var useTilesetCode = defaultUseTilesetCode;
			var useTilesetCodeNode = sequence.LastChildMatching("UseTilesetCode");
			if (useTilesetCodeNode != null)
			{
				useTilesetCode = FieldLoader.GetValue<bool>("UseTilesetCode", useTilesetCodeNode.Value.Value);
				sequence.RemoveNode(useTilesetCodeNode);
			}

			var tilesetOverrides = defaultTilesetOverrides;
			var tilesetOverridesNode = sequence.LastChildMatching("TilesetOverrides");
			if (tilesetOverridesNode != null)
			{
				tilesetOverrides = new Dictionary<string, string>();
				foreach (var kv in defaultTilesetOverrides)
					tilesetOverrides[kv.Key] = kv.Value;

				foreach (var n in tilesetOverridesNode.Value.Nodes)
					tilesetOverrides[n.Key] = n.Value.Value;

				sequence.RemoveNode(tilesetOverridesNode);
			}

			if (sequenceImage.StartsWith("^"))
				return;

			if (!useTilesetExtension && !useTilesetCode)
			{
				if (addExtension)
					sequence.ReplaceValue(sequenceImage + defaultSpriteExtension);

				return;
			}

			var imageNode = new MiniYamlNode("Image", addExtension ? sequenceImage + defaultSpriteExtension : sequenceImage);
			sequence.ReplaceValue("");

			string firstTilesetImage = null;
			foreach (var tileset in modData.DefaultTileSets.Keys)
			{
				var sequenceTilesetImage = sequenceImage;
				if (!tilesetOverrides.TryGetValue(tileset, out var sequenceTileset))
					sequenceTileset = tileset;

				if (useTilesetCode)
					sequenceTilesetImage = sequenceTilesetImage.Substring(0, 1) +
						tilesetCodes[sequenceTileset] +
					    sequenceTilesetImage.Substring(2, sequenceTilesetImage.Length - 2);

				if (addExtension)
					sequenceTilesetImage += useTilesetExtension ? tilesetExtensions[sequenceTileset] : defaultSpriteExtension;

				imageNode.AddNode(tileset, sequenceTilesetImage);
				firstTilesetImage = firstTilesetImage ?? sequenceTilesetImage;
			}

			if (firstTilesetImage != null)
				imageNode.ReplaceValue(firstTilesetImage);

			if (firstTilesetImage != null || !explicitDefaultImage)
				sequence.AddNode(imageNode);
		}
	}
}
