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
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Network;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common
{
	public class TouchIcon
	{
		public ActorInfo Actor;
		public Pair<byte[], byte[]> Data;
		public int DataSwitchRow;
		public List<ProductionItem> Queued;
		public ProductionQueue ProductionQueue;
	}

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

		TouchIcon[] icons = new TouchIcon[10];
		public int DisplayedIconCount { get; private set; }
		public int IconRowOffset = 0;

		public readonly int Columns = 2;

		public readonly string TabClick = null;

		bool iconsDirty;

		Dictionary<ActorInfo, Pair<byte[], byte[]>> iconCache = new Dictionary<ActorInfo, Pair<byte[], byte[]>>();

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

		Pair<byte[], byte[]> LoadIcon(ActorInfo ai)
		{
			int[] ChannelMasks = { 2, 1, 0, 3 };

			var rsi = ai.TraitInfo<RenderSpritesInfo>();
			var icon = new Animation(World, rsi.GetImage(ai, World.Map.Rules.Sequences, null));

			var bi = ai.TraitInfo<BuildableInfo>();
			var palette = worldRenderer.Palette(bi.IconPalette);
			icon.Play(bi.Icon);
			var image = icon.Image;
			var sheet = image.Sheet;
			var imageData = sheet.GetData();

			var normal = new byte[120 * 80 * 2];
			var dark = new byte[120 * 80 * 2];

			for (var j = 0; j < 40; j++)
			{
				for (var k = 0; k < 60; k++)
				{
					var sourceIndex = 4 * ((image.Bounds.Top + j + 2) * sheet.Size.Width + image.Bounds.Left + k + 2) + ChannelMasks[(int)image.Channel];
					var c = palette.Palette.GetColor(imageData[sourceIndex]);
					var normalHigh = (byte)((c.R & 0xF8) | (c.G >> 5));
					var normalLow = (byte)(((c.G & 0xFC) << 3) | (c.B >> 3));
					var darkHigh = (byte)((c.R & 0xE0) >> 2 | (c.G >> 7));
					var darkLow = (byte)(((c.G & 0xF0) << 1) | (c.B >> 5));
					//var normal565 = (ushort)(((c.R & 0xF8) << 8) | ((c.G & 0xFC) << 3) | (c.B >> 3));
					//var dark565 = (ushort)(((c.R & 0xE0) << 6) | ((c.G & 0xF0) << 1) | (c.B >> 5));

					normal[2 * (120 * j + 2 * k) + 0] = normalLow;
					normal[2 * (120 * j + 2 * k) + 1] = normalHigh;
					normal[2 * (120 * j + 2 * k) + 2] = normalLow;
					normal[2 * (120 * j + 2 * k) + 3] = normalHigh;

					dark[2 * (120 * j + 2 * k) + 0] = darkLow;
					dark[2 * (120 * j + 2 * k) + 1] = darkHigh;
					dark[2 * (120 * j + 2 * k) + 2] = darkLow;
					dark[2 * (120 * j + 2 * k) + 3] = darkHigh;
				}
			}

			return new Pair<byte[], byte[]>(normal, dark);
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

			var buildables = AllBuildables.Skip(IconRowOffset * Columns).Take(10).ToList();
			DisplayedIconCount = buildables.Count;

			var buildingSomething = currentQueue.AllQueued().Any();
			var buildableItems = currentQueue.BuildableItems();

			for (var i = 0; i < 10; i++)
			{
				if (i >= DisplayedIconCount)
				{
					icons[i] = null;
					continue;
				}

				var item = buildables[i];
				var queued = currentQueue.AllQueued().Where(a => a.Item == item.Name).ToList();
				if (!iconCache.ContainsKey(item))
					iconCache[item] = LoadIcon(item);

				var switchRow = buildingSomething || !buildableItems.Any(a => a.Name == item.Name) ? 0 : 80;
				if (queued.Count > 0)
				{
					var first = queued[0];
					switchRow = (first.TotalTime - first.RemainingTime) * 80 / first.TotalTime;
				}

				icons[i] = new TouchIcon()
				{
					Actor = item,
					Data = iconCache[item],
					DataSwitchRow = switchRow,
					Queued = queued,
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
					// Check if it is an icon
					var i = (c.X - 80) / 120;
					var j = (c.Y - 30) / 80;

					Console.WriteLine("{0} {1} {2}", c, i, j);

					if (i >= 0 && i < 2 && j >= 0 && j < 5)
					{
						var icon = icons[j * 2 + i];
						if (icon != null)
						{
							var item = icon.Queued.FirstOrDefault();
							HandleLeftClick(item, icon, 1);
						}
					}
				}

				clicks.Clear();
			}
		}

		protected bool PickUpCompletedBuildingIcon(TouchIcon icon, ProductionItem item)
		{
			var actor = World.Map.Rules.Actors[icon.Actor.Name];

			if (item != null && item.Done && actor.HasTraitInfo<BuildingInfo>())
			{
				World.OrderGenerator = new PlaceBuildingOrderGenerator(CurrentQueue, icon.Actor.Name, worldRenderer);
				return true;
			}

			return false;
		}

		bool HandleLeftClick(ProductionItem item, TouchIcon icon, int handleCount)
		{
			if (PickUpCompletedBuildingIcon(icon, item))
			{
				Game.Sound.Play(SoundType.UI, TabClick);
				return true;
			}

			if (item != null && item.Paused)
			{
				// Resume a paused item
				Game.Sound.Play(SoundType.UI, TabClick);
				World.IssueOrder(Order.PauseProduction(CurrentQueue.Actor, icon.Actor.Name, false));
				return true;
			}

			if (CurrentQueue.BuildableItems().Any(a => a.Name == icon.Actor.Name))
			{
				// Queue a new item
				Game.Sound.Play(SoundType.UI, TabClick);
				Game.Sound.PlayNotification(World.Map.Rules, World.LocalPlayer, "Speech", CurrentQueue.Info.QueuedAudio, World.LocalPlayer.Faction.InternalName);
				World.IssueOrder(Order.StartProduction(CurrentQueue.Actor, icon.Actor.Name, handleCount));
				return true;
			}

			return false;
		}

		unsafe void RenderLoop()
		{
			using (var mmf = MemoryMappedFile.CreateFromFile("/dev/fb1", FileMode.OpenOrCreate))
			{
				using (var accessor = mmf.CreateViewAccessor(0, 480 * 320 * 2, MemoryMappedFileAccess.ReadWrite))
				{
					var blankRow = new byte[240];

					while (!stopRenderer)
					{
						// TODO: race conditions city
						if (iconsDirty)
						{
							byte* ptr = (byte*)0;
							accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);

							for (var i = 0; i < 10; i++)
							{
								var x = (i % 2) * 120 + 80;
								var y = (i / 2) * 80 + 35;

								var icon = icons[i];
								if (icon != null)
								{
									// Blit line by line
									for (var j = 0; j < 80; j++)
									{
										var iconData = j < icon.DataSwitchRow ? icon.Data.First : icon.Data.Second;
										Marshal.Copy(iconData, (j / 2) * 240, IntPtr.Add(new IntPtr(ptr), (j + y) * 320 * 2 + 2 * x), 240);
									}
								}
								else
									for (var j = 0; j < 80; j++)
										Marshal.Copy(blankRow, 0, IntPtr.Add(new IntPtr(ptr), (j + y) * 320 * 2 + 2 * x), 240);
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
			using (var inputStream = new FileStream("/dev/input/event3", FileMode.Open, FileAccess.Read))
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
						var x = (3900 - pos) * 32 / 360;
						var y = (3900 - (int)data) * 48 / 360;
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
