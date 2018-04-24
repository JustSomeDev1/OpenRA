#region Copyright & License Information
/*
 * Copyright 2007-2018 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Network;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common
{
	public class TouchScreenPaletteWidget : Widget
	{
		readonly Queue<int2> clicks = new Queue<int2>();
		readonly object syncInput = new object();
		readonly Thread renderer;
		bool stopRenderer;

		public int TotalIconCount { get; private set; }
		ProductionQueue currentQueue;
		readonly WorldRenderer worldRenderer;
		readonly World World;

		ProductionIcon[] icons = new ProductionIcon[12];
		public int DisplayedIconCount { get; private set; }
		public int IconRowOffset = 0;

		public readonly int Columns = 4;

		bool iconsDirty;

		public ProductionQueue CurrentQueue
		{
			get { return currentQueue; }
			set { currentQueue = value; RefreshIcons(); }
		}

		[ObjectCreator.UseCtor]
		public TouchScreenPaletteWidget(ModData modData, OrderManager orderManager, World world, WorldRenderer worldRenderer)
		{
			World = world;
			this.worldRenderer = worldRenderer;

			var inputProcessor = new Thread(ProcessRawInput)
			{
				Name = "Second Screen Input Handler",
				IsBackground = true
			};

			inputProcessor.Start();

			renderer = new Thread(RenderLoop)
			{
				Name = "Second Screen Renderer"
			};

			renderer.Start();
		}

		public IEnumerable<ActorInfo> AllBuildables
		{
			get
			{
				if (CurrentQueue == null)
					return Enumerable.Empty<ActorInfo>();

				return CurrentQueue.AllItems().OrderBy(a => a.TraitInfo<BuildableInfo>().BuildPaletteOrder);
			}
		}

		public void RefreshIcons()
		{
			var producer = CurrentQueue != null ? CurrentQueue.MostLikelyProducer() : default(TraitPair<Production>);
			if (CurrentQueue == null || producer.Trait == null)
			{
				DisplayedIconCount = 0;
				return;
			}

			var oldIconCount = DisplayedIconCount;
			DisplayedIconCount = 0;

			var rb = RenderBounds;
			var faction = producer.Trait.Faction;

			var buildables = AllBuildables.Skip(IconRowOffset * Columns).Take(12).ToList();
			DisplayedIconCount = buildables.Count;

			for (var i = 0; i < 12; i++)
			{
				if (i >= DisplayedIconCount)
				{
					icons[i] = null;
					continue;
				}

				var item = buildables[i];
				var rsi = item.TraitInfo<RenderSpritesInfo>();
				var icon = new Animation(World, rsi.GetImage(item, World.Map.Rules.Sequences, faction));
				var bi = item.TraitInfo<BuildableInfo>();
				icon.Play(bi.Icon);

				icons[i] = new ProductionIcon()
				{
					Actor = item,
					Name = item.Name,
					Sprite = icon.Image,
					Palette = worldRenderer.Palette(bi.IconPalette),
					//IconClockPalette = worldRenderer.Palette(ClockPalette),
					//IconDarkenPalette = worldRenderer.Palette(NotBuildablePalette),
					//Pos = new int2(x, y).ToFloat2(),
					Queued = currentQueue.AllQueued().Where(a => a.Item == item.Name).ToList(),
					ProductionQueue = currentQueue
				};
			}

			iconsDirty = true;
		}

		public override void Tick()
		{
			TotalIconCount = AllBuildables.Count();

			if (CurrentQueue != null && !CurrentQueue.Actor.IsInWorld)
				CurrentQueue = null;

			if (CurrentQueue != null)
				RefreshIcons();

			lock (syncInput)
			{
				foreach (var c in clicks)
				{
					// TODO: Handle taps
				}
			}
		}

		unsafe void RenderLoop()
		{
			int[] ChannelMasks = { 2, 1, 0, 3 };

			using (var mmf = MemoryMappedFile.CreateFromFile("/dev/fb1", FileMode.OpenOrCreate))
			{
				using (var accessor = mmf.CreateViewAccessor(0, 480 * 320 * 2, MemoryMappedFileAccess.ReadWrite))
				{
					while (!stopRenderer)
					{
						// TODO: race conditions city
						if (iconsDirty)
						{
							byte* ptr = (byte*)0;
							accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);

							for (var i = 0; i < 12; i++)
							{
								var icon = icons[i];
								if (icon == null)
									continue;

								var x = (i % 4) * 120;
								var y = (i / 4) * 80 + 74;

								Console.WriteLine("{0} {1} {2}", i, x, y);

								var iconData = icon.Sprite.Sheet.GetData();

								// Blit line by line
								for (var j = 0; j < 40; j++)
								{
									for (var k = 0; k < 60; k++)
									{
										//var sourceIndex = (icon.Sprite.Bounds.Top * icon.Sprite.Sheet.Size.Width + icon.Sprite.Bounds.Left) * 4 + ChannelMasks[(int)icon.Sprite.Channel];
										//var c = icon.Palette.Palette.GetColor(iconData[sourceIndex]);
										var c = Color.RoyalBlue;
										ptr[4 * (y * 240 + x)] = (byte)((c.R & 0xF8) | c.G >> 5);
										ptr[4 * (y * 240 + x) + 1] = (byte)(((c.G & 0xFC) << 3) | (c.B >> 3));
										ptr[4 * (y * 240 + x) + 2] = (byte)((c.R & 0xF8) | c.G >> 5);
										ptr[4 * (y * 240 + x) + 3] = (byte)(((c.G & 0xFC) << 3) | (c.B >> 3));
									}
								}
							}

							accessor.SafeMemoryMappedViewHandle.ReleasePointer();

							iconsDirty = false;
						}
						else
							Thread.Sleep(10);
					}

					// Blank screen
					Fill(accessor, 0);
				}
			}
		}

		unsafe void Fill(MemoryMappedViewAccessor accessor, ushort color)
		{
			byte* ptr = (byte*)0;

			var bytes = new[] { (byte)(color >> 8), (byte)color };
			accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
			for (var i = 0; i < 480 * 320 * 2; i += 2)
				Marshal.Copy(bytes, 0, IntPtr.Add(new IntPtr(ptr), i), 2);
			accessor.SafeMemoryMappedViewHandle.ReleasePointer();
		}

		void ProcessRawInput()
		{
			// TODO: This will change on reboot
			using (var inputStream = new FileStream("/dev/input/event2", FileMode.Open, FileAccess.Read))
			{
				var pos = 0;
				var triggered = false;
				while (true)
				{
					// skip time
					inputStream.ReadUInt32();
					inputStream.ReadUInt32();

					var type = inputStream.ReadUInt16();
					var code = inputStream.ReadUInt16();
					var data = inputStream.ReadUInt32();

					if (type == 1 && code == 330 && data == 1)
						triggered = true;
					if (triggered && type == 3 && code == 0)
						pos = (int)data;
					else if (triggered && type == 3 && code == 1)
					{
						var x = -((int)data - 3920) * 48 / 360;
						var y = (pos - 270) * 32 / 360;
						clicks.Enqueue(new int2(x, y));
						triggered = false;
					}
				}
			}
		}

		public override void Removed()
		{
			stopRenderer = true;
			base.Removed();
		}

	}
}
