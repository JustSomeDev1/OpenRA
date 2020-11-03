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
using OpenRA.Graphics;

namespace OpenRA.Mods.Common.Graphics
{
	public class TilesetSpecificSpriteSequenceLoader : DefaultSpriteSequenceLoader
	{
		public TilesetSpecificSpriteSequenceLoader(ModData modData)
			: base(modData) { }

		public override ISpriteSequence CreateSequence(ModData modData, string tileSet, SpriteCache cache, string sequence, string animation, MiniYaml info)
		{
			return new TilesetSpecificSpriteSequence(modData, tileSet, cache, this, sequence, animation, info);
		}
	}

	public class TilesetSpecificSpriteSequence : DefaultSpriteSequence
	{
		public TilesetSpecificSpriteSequence(ModData modData, string tileSet, SpriteCache cache, ISpriteSequenceLoader loader, string sequence, string animation, MiniYaml info)
			: base(modData, tileSet, cache, loader, sequence, animation, info) { }

		protected override string GetSpriteSrc(ModData modData, string tileSet, string sequence, string animation, string sprite, Dictionary<string, MiniYaml> d)
		{
			if (d.TryGetValue("Image", out var imageNode))
			{
				var image = imageNode.Nodes.FirstOrDefault(n => n.Key == tileSet)?.Value.Value ?? imageNode.Value;
				if (!string.IsNullOrEmpty(image))
					return image;
			}

			return base.GetSpriteSrc(modData, tileSet, sequence, animation, sprite, d);
		}
	}
}
