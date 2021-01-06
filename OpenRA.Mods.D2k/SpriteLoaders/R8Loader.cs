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
using System.IO;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Primitives;

namespace OpenRA.Mods.D2k.SpriteLoaders
{
	public class R8Loader : ISpriteLoader
	{
		class R8Frame : ISpriteFrame
		{
			public SpriteFrameType Type { get; private set; }
			public Size Size { get; private set; }
			public Size FrameSize { get; private set; }
			public float2 Offset { get; private set; }
			public byte[] Data { get; set; }
			public bool DisableExportPadding { get { return true; } }

			public R8Frame(Stream s, Dictionary<uint, uint[]> palettes, int frameno)
			{
				// Scan forward until we find some data
				var type = s.ReadUInt8();
				while (type == 0)
					type = s.ReadUInt8();

				var width = s.ReadInt32();
				var height = s.ReadInt32();
				var x = s.ReadInt32();
				var y = s.ReadInt32();

				Size = new Size(width, height);
				Offset = new int2(width / 2 - x, height / 2 - y);

				var imageHandle = s.ReadUInt32();
				var paletteHandle = s.ReadUInt32();
				var bpp = s.ReadUInt8();
				if (bpp != 8 && bpp != 16)
					throw new InvalidDataException("Error: {0} bits per pixel are not supported.".F(bpp));

				var frameHeight = s.ReadUInt8();
				var frameWidth = s.ReadUInt8();
				FrameSize = new Size(frameWidth, frameHeight);

				// Skip alignment byte
				s.ReadUInt8();

				if (bpp == 16)
				{
					Data = new byte[width * height * 4];
					Type = SpriteFrameType.Bgra32;

					unsafe
					{
						fixed (byte* bd = &Data[0])
						{
							var data = (uint*)bd;
							for (var i = 0; i < width * height; i++)
							{
								var packed = s.ReadUInt16();
								data[i] = (uint)((0xFF << 24) | ((packed & 0x7C00) << 9) | ((packed & 0x3E0) << 6) | ((packed & 0x1f) << 3));
							}
						}
					}
				}
				else
				{
					Data = s.ReadBytes(width * height);
					Type = SpriteFrameType.Indexed8;
				}

				// Read palette
				if (type == 1 && paletteHandle != 0)
				{
					// Skip header
					var paletteBase = s.ReadUInt32();
					var paletteOffset = s.ReadUInt32();

					var pd = new uint[256];
					for (var i = 0; i < 256; i++)
					{
						var packed = s.ReadUInt16();
						pd[i] = (uint)((0xFF << 24) | ((packed & 0x7C00) << 9) | ((packed & 0x3E0) << 6) | ((packed & 0x1f) << 3));
					}

					// Remap index 0 to transparent
					pd[0] = 0;

					// Remap index 1 to shadow
					pd[1] = 140u << 24;

					palettes[paletteHandle] = pd;
				}

				// Resolve embedded palettes to RGBA sprites
				uint[] palette;
				var validPalette = palettes.TryGetValue(paletteHandle, out palette);
				if (paletteHandle != 0 && validPalette)
				{
					var oldData = Data;
					Type = SpriteFrameType.Bgra32;
					Data = new byte[width * height * 4];

					unsafe
					{
						fixed (byte* bd = &Data[0])
						{
							var data = (uint*)bd;
							for (var i = 0; i < width * height; i++)
								data[i] = palette[oldData[i]];
						}
					}
				}
			}
		}

		bool IsR8(Stream s)
		{
			var start = s.Position;

			// First byte is nonzero
			if (s.ReadUInt8() == 0)
			{
				s.Position = start;
				return false;
			}

			// Check the format of the first frame
			s.Position = start + 25;
			var d = s.ReadUInt8();

			s.Position = start;
			return d == 8 || d == 16;
		}

		public bool TryParseSprite(Stream s, out ISpriteFrame[] frames, out TypeDictionary metadata)
		{
			metadata = null;
			if (!IsR8(s))
			{
				frames = null;
				return false;
			}

			var start = s.Position;
			var tmp = new List<R8Frame>();
			var i = 0;

			Dictionary<uint, uint[]> palettes = new Dictionary<uint, uint[]>();
			while (s.Position < s.Length)
				tmp.Add(new R8Frame(s, palettes, i++));

			s.Position = start;

			frames = tmp.ToArray();

			return true;
		}
	}
}
