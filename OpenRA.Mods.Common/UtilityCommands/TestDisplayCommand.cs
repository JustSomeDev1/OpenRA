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
using System.Threading.Tasks;
using System.Threading;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.IO;
using System.Drawing;
using System.Collections.Generic;

namespace OpenRA.Mods.Common.UtilityCommands
{
	class TestDisplayCommand : IUtilityCommand
	{
		string IUtilityCommand.Name { get { return "--test-display"; } }

		bool IUtilityCommand.ValidateArguments(string[] args) { return true; }

		object syncInput = new object();
		Queue<int2> clicks = new Queue<int2>();

		public ushort Color565(byte r, byte g, byte b)
		{
			return (ushort)(((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3));
		}

		const int Length = 480 * 320 * 2;

		unsafe void Fill(MemoryMappedViewAccessor accessor, ushort color)
		{
			byte* ptr = (byte*)0;

			var bytes = new[] { (byte)(color >> 8), (byte)color };
			accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
			for (var i = 0; i < Length; i += 2)
				Marshal.Copy(bytes, 0, IntPtr.Add(new IntPtr(ptr), i), 2);
			accessor.SafeMemoryMappedViewHandle.ReleasePointer();
		}

		unsafe void Blit(MemoryMappedViewAccessor accessor, byte[] data)
		{
			byte* ptr = (byte*)0;

			accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
			Marshal.Copy(data, 0, new IntPtr(ptr), data.Length);
			accessor.SafeMemoryMappedViewHandle.ReleasePointer();
		}

		byte[] Load(string path)
		{
			using (var bitmap = new Bitmap(path))
			{
				var image = new byte[bitmap.Size.Width * bitmap.Size.Height * 2];
				for (var y = 0; y < bitmap.Size.Height; y++)
				{
					for (var x = 0; x < bitmap.Size.Width; x++)
					{
						var px = bitmap.GetPixel(x, y);
						var foo = Color565(px.R, px.G, px.B);
						var i = 2 * y * bitmap.Size.Width + 2 * x;
						image[i] = (byte)foo;
						image[i+1] = (byte)(foo >> 8);
					}
				}
				return image;
			}
		}

		void ProcessInput()
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

		[Desc("blah", "blah")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			// HACK: The engine code assumes that Game.modData is set.
			Game.ModData = utility.ModData;

			var inputProcessor = new Thread(ProcessInput)
			{
				Name = "Touch handler",
				IsBackground = true
			};

			inputProcessor.Start();

			var action = Load("mockup2.png");
			var palette = Load("mockup.png");

			using (var mmf = MemoryMappedFile.CreateFromFile("/dev/fb1", FileMode.OpenOrCreate))
			{
				using (var accessor = mmf.CreateViewAccessor(0, Length, MemoryMappedFileAccess.ReadWrite))
				{
					while (true)
					{
						Blit(accessor, action);
						Thread.Sleep(500);
						Blit(accessor, palette);
						Thread.Sleep(500);
						Fill(accessor, Color565(0, 0, 0));
						Thread.Sleep(500);

						lock (syncInput)
						{
							foreach (var c in clicks)
								Console.WriteLine("Click: {0}", c);
							clicks.Clear();
						}
					}
				}
			}
		}
	}
}
