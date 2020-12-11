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

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Primitives;

namespace OpenRA.Graphics
{
	public class PlayerColorRemap : IPaletteRemap
	{
		readonly int[] remapIndices;
		readonly float hueOffset;
		readonly float saturationOffset;
		readonly float valueOffset;

		public PlayerColorRemap(int[] remapIndices, float hueOffset, float saturationOffset, float valueOffset)
		{
			this.remapIndices = remapIndices;
			this.hueOffset = hueOffset;
			this.saturationOffset = saturationOffset;
			this.valueOffset = valueOffset;
		}

		public Color GetRemappedColor(Color original, int index)
		{
			if (!remapIndices.Contains(index))
				return original;

			original.ToAhsv(out var a, out var h, out var s, out var v);
			return Color.FromAhsv(a, (h + hueOffset) % 1, (s + saturationOffset).Clamp(0, 1), (v + valueOffset).Clamp(0, 1));
		}
	}
}
