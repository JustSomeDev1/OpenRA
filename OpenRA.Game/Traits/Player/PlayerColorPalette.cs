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

using OpenRA.Graphics;
using OpenRA.Primitives;

namespace OpenRA.Traits
{
	[Desc("Add this to the Player actor definition.")]
	public class PlayerColorPaletteInfo : TraitInfo
	{
		[PaletteReference]
		[Desc("The name of the palette to base off.")]
		public readonly string BasePalette = null;

		[PaletteDefinition(true)]
		[Desc("The prefix for the resulting player palettes")]
		public readonly string BaseName = "player";

		[FieldLoader.Require]
		[Desc("The palette indices to remap to player color.",
		"The first index in this list is taken as the reference color that is exactly matched to the player.",
		"The remaining indices are changed to keep the same relative color offset to the reference index.")]
		public readonly int[] RemapIndex = { };

		[Desc("Allow palette modifiers to change the palette.")]
		public readonly bool AllowModifiers = true;

		public override object Create(ActorInitializer init) { return new PlayerColorPalette(this); }
	}

	public class PlayerColorPalette : ILoadsPlayerPalettes
	{
		readonly PlayerColorPaletteInfo info;

		public PlayerColorPalette(PlayerColorPaletteInfo info)
		{
			this.info = info;
		}

		public void LoadPlayerPalettes(WorldRenderer wr, string playerName, Color color, bool replaceExisting)
		{
			var basePal = wr.Palette(info.BasePalette).Palette;
			var referenceColor = basePal.GetColor(info.RemapIndex[0]);

			referenceColor.ToAhsv(out _, out var rh, out var rs, out var rv);
			color.ToAhsv(out _, out var h, out var s, out var v);

			var remap = new PlayerColorRemap(info.RemapIndex, h - rh, s - rs, v - rv);
			var pal = new ImmutablePalette(basePal, remap);
			wr.AddPalette(info.BaseName + playerName, pal, info.AllowModifiers, replaceExisting);
		}
	}
}
