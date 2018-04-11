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
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	[SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1203:ConstantsMustAppearBeforeFields",
		Justification = "SystemInformation version should be defined next to the dictionary it refers to.")]
	public class MainMenuLogic : ChromeLogic
	{
		protected enum MenuType { Main, Singleplayer, Extras, SystemInfoPrompt, None }

		protected enum MenuPanel { None, Missions, Skirmish, Multiplayer, MapEditor, Replays }

		protected MenuType menuType = MenuType.Main;
		readonly Widget rootMenu;
		protected static MenuPanel lastGameState = MenuPanel.None;

		// Increment the version number when adding new stats
		const int SystemInformationVersion = 3;
		Dictionary<string, Pair<string, string>> GetSystemInformation()
		{
			var lang = System.Globalization.CultureInfo.InstalledUICulture.TwoLetterISOLanguageName;
			return new Dictionary<string, Pair<string, string>>()
			{
				{ "id", Pair.New("Anonymous ID", Game.Settings.Debug.UUID) },
				{ "platform", Pair.New("OS Type", Platform.CurrentPlatform.ToString()) },
				{ "os", Pair.New("OS Version", Environment.OSVersion.ToString()) },
				{ "x64", Pair.New("OS is 64 bit", Environment.Is64BitOperatingSystem.ToString()) },
				{ "x64process", Pair.New("Process is 64 bit", Environment.Is64BitProcess.ToString()) },
				{ "runtime", Pair.New(".NET Runtime", Platform.RuntimeVersion) },
				{ "gl", Pair.New("OpenGL Version", Game.Renderer.GLVersion) },
				{ "windowsize", Pair.New("Window Size", "{0}x{1}".F(Game.Renderer.Resolution.Width, Game.Renderer.Resolution.Height)) },
				{ "windowscale", Pair.New("Window Scale", Game.Renderer.WindowScale.ToString("F2", CultureInfo.InvariantCulture)) },
				{ "lang", Pair.New("System Language", lang) }
			};
		}

		void SwitchMenu(MenuType type)
		{
			menuType = type;

			// Update button mouseover
			Game.RunAfterTick(Ui.ResetTooltips);
		}

		[ObjectCreator.UseCtor]
		public MainMenuLogic(Widget widget, World world, ModData modData)
		{
			rootMenu = widget;
			rootMenu.Get<LabelWidget>("VERSION_LABEL").Text = modData.Manifest.Metadata.Version;

			// Menu buttons
			var mainMenu = widget.Get("MAIN_MENU");
			mainMenu.IsVisible = () => menuType == MenuType.Main;

			mainMenu.Get<ButtonWidget>("SINGLEPLAYER_BUTTON").OnClick = () => SwitchMenu(MenuType.Singleplayer);

			mainMenu.Get<ButtonWidget>("MULTIPLAYER_BUTTON").OnClick = OpenMultiplayerPanel;

			mainMenu.Get<ButtonWidget>("CONTENT_BUTTON").OnClick = () =>
			{
				// Switching mods changes the world state (by disposing it),
				// so we can't do this inside the input handler.
				Game.RunAfterTick(() =>
				{
					var content = modData.Manifest.Get<ModContent>();
					Game.InitializeMod(content.ContentInstallerMod, new Arguments(new[] { "Content.Mod=" + modData.Manifest.Id }));
				});
			};

			mainMenu.Get<ButtonWidget>("SETTINGS_BUTTON").OnClick = () =>
			{
				SwitchMenu(MenuType.None);
				Game.OpenWindow("SETTINGS_PANEL", new WidgetArgs
				{
					{ "onExit", () => SwitchMenu(MenuType.Main) }
				});
			};

			mainMenu.Get<ButtonWidget>("EXTRAS_BUTTON").OnClick = () => SwitchMenu(MenuType.Extras);

			mainMenu.Get<ButtonWidget>("QUIT_BUTTON").OnClick = Game.Exit;

			// Singleplayer menu
			var singleplayerMenu = widget.Get("SINGLEPLAYER_MENU");
			singleplayerMenu.IsVisible = () => menuType == MenuType.Singleplayer;

			var missionsButton = singleplayerMenu.Get<ButtonWidget>("MISSIONS_BUTTON");
			missionsButton.OnClick = OpenMissionBrowserPanel;

			var hasCampaign = modData.Manifest.Missions.Any();
			var hasMissions = modData.MapCache
				.Any(p => p.Status == MapStatus.Available && p.Visibility.HasFlag(MapVisibility.MissionSelector));

			missionsButton.Disabled = !hasCampaign && !hasMissions;

			singleplayerMenu.Get<ButtonWidget>("SKIRMISH_BUTTON").OnClick = StartSkirmishGame;

			singleplayerMenu.Get<ButtonWidget>("BACK_BUTTON").OnClick = () => SwitchMenu(MenuType.Main);

			// Extras menu
			var extrasMenu = widget.Get("EXTRAS_MENU");
			extrasMenu.IsVisible = () => menuType == MenuType.Extras;

			extrasMenu.Get<ButtonWidget>("REPLAYS_BUTTON").OnClick = OpenReplayBrowserPanel;

			extrasMenu.Get<ButtonWidget>("MUSIC_BUTTON").OnClick = () =>
			{
				SwitchMenu(MenuType.None);
				Ui.OpenWindow("MUSIC_PANEL", new WidgetArgs
				{
					{ "onExit", () => SwitchMenu(MenuType.Extras) },
					{ "world", world }
				});
			};

			var assetBrowserButton = extrasMenu.GetOrNull<ButtonWidget>("ASSETBROWSER_BUTTON");
			if (assetBrowserButton != null)
				assetBrowserButton.OnClick = () =>
				{
					SwitchMenu(MenuType.None);
					Game.OpenWindow("ASSETBROWSER_PANEL", new WidgetArgs
					{
						{ "onExit", () => SwitchMenu(MenuType.Extras) },
					});
				};

			extrasMenu.Get<ButtonWidget>("CREDITS_BUTTON").OnClick = () =>
			{
				SwitchMenu(MenuType.None);
				Ui.OpenWindow("CREDITS_PANEL", new WidgetArgs
				{
					{ "onExit", () => SwitchMenu(MenuType.Extras) },
				});
			};

			extrasMenu.Get<ButtonWidget>("BACK_BUTTON").OnClick = () => SwitchMenu(MenuType.Main);

			// Loading into the map editor
			Game.BeforeGameStart += RemoveShellmapUI;

			Game.OnRemoteDirectConnect += OnRemoteDirectConnect;

			// Check for updates in the background
			var webServices = modData.Manifest.Get<WebServices>();
			if (Game.Settings.Debug.CheckVersion)
				webServices.CheckModVersion();

			var updateLabel = rootMenu.GetOrNull("UPDATE_NOTICE");
			if (updateLabel != null)
				updateLabel.IsVisible = () => menuType != MenuType.None &&
					menuType != MenuType.SystemInfoPrompt &&
					webServices.ModVersionStatus == ModVersionStatus.Outdated;

			var playerProfile = widget.GetOrNull("PLAYER_PROFILE_CONTAINER");
			if (playerProfile != null)
			{
				Func<bool> minimalProfile = () => Ui.CurrentWindow() != null;
				Game.LoadWidget(world, "LOCAL_PROFILE_PANEL", playerProfile, new WidgetArgs()
				{
					{ "minimalProfile", minimalProfile }
				});
			}

			// System information opt-out prompt
			var sysInfoPrompt = widget.Get("SYSTEM_INFO_PROMPT");
			sysInfoPrompt.IsVisible = () => menuType == MenuType.SystemInfoPrompt;
			if (Game.Settings.Debug.SystemInformationVersionPrompt < SystemInformationVersion)
			{
				menuType = MenuType.SystemInfoPrompt;

				var sysInfoCheckbox = sysInfoPrompt.Get<CheckboxWidget>("SYSINFO_CHECKBOX");
				sysInfoCheckbox.IsChecked = () => Game.Settings.Debug.SendSystemInformation;
				sysInfoCheckbox.OnClick = () => Game.Settings.Debug.SendSystemInformation ^= true;

				var sysInfoData = sysInfoPrompt.Get<ScrollPanelWidget>("SYSINFO_DATA");
				var template = sysInfoData.Get<LabelWidget>("DATA_TEMPLATE");
				sysInfoData.RemoveChildren();

				foreach (var info in GetSystemInformation().Values)
				{
					var label = template.Clone() as LabelWidget;
					var text = info.First + ": " + info.Second;
					label.GetText = () => text;
					sysInfoData.AddChild(label);
				}

				sysInfoPrompt.Get<ButtonWidget>("BACK_BUTTON").OnClick = () =>
				{
					Game.Settings.Debug.SystemInformationVersionPrompt = SystemInformationVersion;
					Game.Settings.Save();
					SwitchMenu(MenuType.Main);
				};
			}
		}

		void OnRemoteDirectConnect(string host, int port)
		{
			SwitchMenu(MenuType.None);
			Ui.OpenWindow("MULTIPLAYER_PANEL", new WidgetArgs
			{
				{ "onStart", RemoveShellmapUI },
				{ "onExit", () => SwitchMenu(MenuType.Main) },
				{ "directConnectHost", host },
				{ "directConnectPort", port },
			});
		}

		void RemoveShellmapUI()
		{
			rootMenu.Parent.RemoveChild(rootMenu);
		}

		void StartSkirmishGame()
		{
			var map = Game.ModData.MapCache.ChooseInitialMap(Game.Settings.Server.Map, Game.CosmeticRandom);
			Game.Settings.Server.Map = map;
			Game.Settings.Save();

			ConnectionLogic.Connect(IPAddress.Loopback.ToString(),
				Game.CreateLocalServer(map),
				"",
				OpenSkirmishLobbyPanel,
				() => { Game.CloseServer(); SwitchMenu(MenuType.Main); });
		}

		void OpenMissionBrowserPanel()
		{
			SwitchMenu(MenuType.None);
			Game.OpenWindow("MISSIONBROWSER_PANEL", new WidgetArgs
			{
				{ "onExit", () => SwitchMenu(MenuType.Singleplayer) },
				{ "onStart", () => { RemoveShellmapUI(); lastGameState = MenuPanel.Missions; } }
			});
		}

		void OpenSkirmishLobbyPanel()
		{
			SwitchMenu(MenuType.None);
			Game.OpenWindow("SERVER_LOBBY", new WidgetArgs
			{
				{ "onExit", () => { Game.Disconnect(); SwitchMenu(MenuType.Singleplayer); } },
				{ "onStart", () => { RemoveShellmapUI(); lastGameState = MenuPanel.Skirmish; } },
				{ "skirmishMode", true }
			});
		}

		void OpenMultiplayerPanel()
		{
			SwitchMenu(MenuType.None);
			Ui.OpenWindow("MULTIPLAYER_PANEL", new WidgetArgs
			{
				{ "onStart", () => { RemoveShellmapUI(); lastGameState = MenuPanel.Multiplayer; } },
				{ "onExit", () => SwitchMenu(MenuType.Main) },
				{ "directConnectHost", null },
				{ "directConnectPort", 0 },
			});
		}

		void OpenReplayBrowserPanel()
		{
			SwitchMenu(MenuType.None);
			Ui.OpenWindow("REPLAYBROWSER_PANEL", new WidgetArgs
			{
				{ "onExit", () => SwitchMenu(MenuType.Extras) },
				{ "onStart", () => { RemoveShellmapUI(); lastGameState = MenuPanel.Replays; } }
			});
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				Game.OnRemoteDirectConnect -= OnRemoteDirectConnect;
				Game.BeforeGameStart -= RemoveShellmapUI;
			}

			Game.OnShellmapLoaded -= OpenMenuBasedOnLastGame;
			base.Dispose(disposing);
		}

		void OpenMenuBasedOnLastGame()
		{
			switch (lastGameState)
			{
				case MenuPanel.Missions:
					OpenMissionBrowserPanel();
					break;

				case MenuPanel.Replays:
					OpenReplayBrowserPanel();
					break;

				case MenuPanel.Skirmish:
					StartSkirmishGame();
					break;

				case MenuPanel.Multiplayer:
					OpenMultiplayerPanel();
					break;
			}

			lastGameState = MenuPanel.None;
		}
	}
}
