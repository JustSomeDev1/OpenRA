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
using System.Linq;

namespace OpenRA.Mods.Common.UpdateRules.Rules
{
	public class RemovePlaceBuildingPalette : UpdateRule
	{
		public override string Name { get { return "*PlaceBuildingPreview palette overrides have been removed."; } }

		public override string Description
		{
			get
			{
				return "The palette overrides on the ActorPreviewPlaceBuildingPreview, FootprintPlaceBuildingPreview\n" +
					"SequencePlaceBuildingPreview, and D2kActorPreviewPlaceBuildingPreview traits have been removed.\n" +
					"New Alpha and LineBuildSegmentAlpha properties have been added in their place.";
			}
		}

		public override IEnumerable<string> UpdateActorNode(ModData modData, MiniYamlNode actorNode)
		{
			foreach (var node in actorNode.ChildrenMatching("ActorPreviewPlaceBuildingPreview"))
			{
				node.RemoveNodes("OverridePalette");
				node.RemoveNodes("OverridePaletteIsPlayerPalette");
				node.RemoveNodes("LineBuildSegmentPalette");
			}

			foreach (var node in actorNode.ChildrenMatching("D2kActorPreviewPlaceBuildingPreview"))
			{
				node.RemoveNodes("OverridePalette");
				node.RemoveNodes("OverridePaletteIsPlayerPalette");
				node.RemoveNodes("LineBuildSegmentPalette");
			}

			foreach (var node in actorNode.ChildrenMatching("FootprintPlaceBuildingPreview"))
				node.RemoveNodes("LineBuildSegmentPalette");

			foreach (var node in actorNode.ChildrenMatching("SequencePlaceBuildingPreview"))
			{
				node.RemoveNodes("SequencePalette");
				node.RemoveNodes("SequencePaletteIsPlayerPalette");
				node.RemoveNodes("LineBuildSegmentPalette");
			}

			yield break;
		}
	}
}
