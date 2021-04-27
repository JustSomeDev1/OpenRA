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
using OpenRA.Mods.Common.FileFormats;

namespace OpenRA.Mods.Cnc.UtilityCommands
{
	class DecodeRAMissionScript : IUtilityCommand
	{
		// References:
		// https://github.com/electronicarts/CnC_Remastered_Collection/
		// https://gamefaqs.gamespot.com/pc/196962-command-and-conquer-red-alert/faqs/1701
		public enum HouseType
		{
			Spain, Greece, USSR, England, Ukraine, Germany, France, Turkey,
			Good, Bad, Neutral, Special,
			Multi1, Multi2, Multi3, Multi4, Multi5, Multi6, Multi7, Multi8
		}

		public class Event
		{
			enum EventType
			{
				NONE, PLAYER_ENTERED, SPIED, THIEVED, DISCOVERED, HOUSE_DISCOVERED, ATTACKED, DESTROYED, ANY, UNITS_DESTROYED,
				BUILDINGS_DESTROYED, ALL_DESTROYED, CREDITS, TIME, MISSION_TIMER_EXPIRED, NBUILDINGS_DESTROYED, NUNITS_DESTROYED,
				NOFACTORIES, EVAC_CIVILIAN, BUILD, BUILD_UNIT, BUILD_INFANTRY, BUILD_AIRCRAFT, LEAVES_MAP, ENTERS_ZONE, CROSS_HORIZONTAL,
				CROSS_VERTICAL, GLOBAL_SET, GLOBAL_CLEAR, FAKES_DESTROYED, LOW_POWER, ALL_BRIDGES_DESTROYED, BUILDING_EXISTS
			}

			enum BuildingType
			{
				ATEK, IRON, WEAP, PDOX, PBOX, HBOX, DOME, GAP, GUN, AGUN, FTUR, FACT, PROC, SILO, HPAD, SAM,
				AFLD, POWR, APWR, STEK, HOSP, BARR, TENT, KENN, FIX, BIO, MISS, SYRD, SPEN, MSLO, FCOM, TSLA,
				WEAF, FACF, SYRF, SPEF, DOMF
			}

			enum UnitType
			{
				_4TNK, _3TNK, _2TNK, _1TNK, APC, MNLY, JEEP, HARV, ARTY, MRJ, MGG, MCV, V2RL, TRUK, ANT1, ANT2, ANT3
			}

			enum InfantryType
			{
				E1, E2, E3, E4, E6, E7, SPY, THF, MEDI, GNRL, DOG,
				C1, C2, C3, C4, C5, C6, C7, C8, C9, C10, EINSTEIN, DELPHI, CHAN
			}

			enum AircraftType
			{
				TRAN, BADR, U2, MIG, YAK, HELI, HIND
			}

			readonly EventType type;
			readonly int team;
			readonly int data;

			public Event(string typeToken, string teamToken, string dataToken)
			{
				type = FieldLoader.GetValue<EventType>("type", typeToken);
				team = FieldLoader.GetValue<int>("team", teamToken);
				data = FieldLoader.GetValue<int>("data", dataToken);

				// Fix weirdly formatted data caused by union usage in the original game
				if (type != EventType.CREDITS && type != EventType.TIME && type != EventType.NBUILDINGS_DESTROYED && type != EventType.NUNITS_DESTROYED)
					data = data & 0xFF;
			}

			public string ToString(HouseType house, List<TeamType> teamTypes)
			{
				switch (type)
				{
					case EventType.NONE: return null;
					case EventType.PLAYER_ENTERED: return "Attached cell entered or attached building captured by house \"{0}\"".F((HouseType)data);
					case EventType.SPIED: return "Attached building Infiltrated by spy";
					case EventType.THIEVED: return "Attached building Infiltrated by thief owned by house \"{0}\"".F((HouseType)data);
					case EventType.DISCOVERED: return "Attached unit or building discovered by the player";
					case EventType.HOUSE_DISCOVERED: return "Any unit or building owned by house \"{0}\" discovered by the player".F((HouseType)data);
					case EventType.ATTACKED: return "Attached unit or building is attacked";
					case EventType.DESTROYED: return "Attached unit or building is destroyed";
					case EventType.ANY: return "Any other event triggers";
					case EventType.UNITS_DESTROYED: return "All units owned by house \"{0}\" are destroyed".F((HouseType)data);
					case EventType.BUILDINGS_DESTROYED: return "All buildings (excluding civilian) owned by house \"{0}\" are destroyed".F((HouseType)data);
					case EventType.ALL_DESTROYED: return "All buildings (excluding civilian) and units owned by house \"{0}\" are destroyed".F((HouseType)data);
					case EventType.CREDITS: return "House \"{0}\" has more than {1} credits".F(house, data);
					case EventType.TIME: return "{0:F1} minutes have elapsed".F(data / 10f);
					case EventType.MISSION_TIMER_EXPIRED: return "Mission timer expired";
					case EventType.NBUILDINGS_DESTROYED: return "{0} buildings (including civilian) owned by house \"{1}\" are destroyed".F(data, house);
					case EventType.NUNITS_DESTROYED: return "{0} units owned by house \"{1}\" are destroyed".F(data, house);
					case EventType.NOFACTORIES: return "House \"{0}\" has not buildings of type FACT,AFLD,BARR,TENT,WEAP remaining".F(house);
					case EventType.EVAC_CIVILIAN: return "Civilian unit owned by \"{0}\" left the map or loaded into helicopter.".F(house);
					case EventType.BUILD: return "House \"{0}\" has built building of type \"{1}\"".F(house, (BuildingType)data);
					case EventType.BUILD_UNIT: return "House \"{0}\" has built vehicle of type \"{1}\"".F(house, (UnitType)data);
					case EventType.BUILD_INFANTRY: return "House \"{0}\" has built infantry of type \"{1}\"".F(house, (InfantryType)data);
					case EventType.BUILD_AIRCRAFT: return "House \"{0}\" has built aircraft of type \"{1}\"".F(house, (AircraftType)data);
					case EventType.LEAVES_MAP: return "Team \"{0}\" leaves the map".F(teamTypes[team].Name);
					case EventType.ENTERS_ZONE: return "A unit owned by house \"{0}\" enters zone of attached cell".F((HouseType)data);
					case EventType.CROSS_HORIZONTAL: return "A unit owned by house \"{0}\" crosses a horizontal line through the attached cell".F((HouseType)data);
					case EventType.CROSS_VERTICAL: return "A unit owned by house \"{0}\" crosses a vertical line through the attached cell".F((HouseType)data);
					case EventType.GLOBAL_SET: return "Global scenario variable {0} is set".F(data);
					case EventType.GLOBAL_CLEAR: return "Global scenario variable {0} is not set".F(data);
					case EventType.FAKES_DESTROYED: return "All fake structures have been destroyed (does not work!)";
					case EventType.LOW_POWER: return "House \"{0}\" is low power".F((HouseType)data);
					case EventType.ALL_BRIDGES_DESTROYED: return "All bridges on map destroyed";
					case EventType.BUILDING_EXISTS: return "House \"{0}\" owns building of type \"{1}\"".F(house, (BuildingType)data);
					default:
						return "Type: {0} Team: {1} Data: {2}".F(type, team, data);
				}
			}
		}

		public class Action
		{
			enum ActionType
			{
				NONE, WIN, LOSE, BEGIN_PRODUCTION, CREATE_TEAM, DESTROY_TEAM, ALL_HUNT, REINFORCEMENTS, DZ,
				FIRE_SALE, PLAY_MOVIE, TEXT_TRIGGER, DESTROY_TRIGGER, AUTOCREATE, WINLOSE, ALLOWWIN, REVEAL_ALL,
				REVEAL_SOME, REVEAL_ZONE, PLAY_SOUND, PLAY_MUSIC, PLAY_SPEECH, FORCE_TRIGGER, START_TIMER,
				STOP_TIMER, ADD_TIMER, SUB_TIMER, SET_TIMER, SET_GLOBAL, CLEAR_GLOBAL, BASE_BUILDING,
				CREEP_SHADOW, DESTROY_OBJECT, ONE_SPECIAL, FULL_SPECIAL, PREFERRED_TARGET, LAUNCH_NUKES
			}

			enum MovieType
			{
				AAGUN, MIG, SFROZEN, AIRFIELD, BATTLE, BMAP, BOMBRUN, DPTHCHRG, GRVESTNE, MONTPASS, MTNKFACT, CRONTEST,
				OILDRUM, ALLYEND, RADRRAID, SHIPYARD, SHORBOMB, SITDUCK, SLNTSRVC, SNOWBASE, EXECUTE, REDINTRO, NUKESTOK,
				V2ROCKET, SEARCH, BINOC, ELEVATOR, FROZEN, MCV, SHIPSINK, SOVMCV, TRINITY, ALLYMORF, APCESCPE, BRDGTILT,
				CRONFAIL, STRAFE, DESTROYR, DOUBLE, FLARE, SNSTRAFE, LANDING, ONTHPRWL, OVERRUN, SNOWBOMB, SOVCEMET,
				TAKE_OFF, TESLA, SOVIET8, SPOTTER, ALLY1, ALLY2, ALLY4, SOVFINAL, ASSESS, SOVIET10, DUD, MCV_LAND,
				MCVBRDGE, PERISCOP, SHORBOM1, SHORBOM2, SOVBATL, SOVTSTAR, AFTRMATH, SOVIET11, MASASSLT, ENGLISH,
				SOVIET1, SOVIET2, SOVIET3, SOVIET4, SOVIET5, SOVIET6, SOVIET7, PROLOG, AVERTED, COUNTDWN, MOVINGIN,
				ALLY10, ALLY12, ALLY5, ALLY6, ALLY8, TANYA1, TANYA2, ALLY10B, ALLY11, ALLY14, ALLY9, SPY, TOOFAR,
				SOVIET12, SOVIET13, SOVIET9, BEACHEAD, SOVIET14, SIZZLE, SIZZLE2, ANTEND, ANTINTRO
			}

			enum SoundType
			{
				GIRLOKAY, GIRLYEAH, GUYOKAY1, GUYYEAH1, MINELAY1, ACKNO, AFFIRM1, AWAIT1, EAFFIRM1, EENGIN1, NOPROB,
				READY, REPORT1, RITAWAY, ROGER, UGOTIT, VEHIC1, YESSIR1, DEDMAN1, DEDMAN2, DEDMAN3, DEDMAN4, DEDMAN5,
				DEDMAN6, DEDMAN7, DEDMAN8, DEDMAN10, CHRONO2, CANNON1, CANNON2, IRONCUR9, EMOVOUT1, SONPULSE, SANDBAG2,
				MINEBLO1, CHUTE1, DOGY1, DOGW5, DOGG5P, FIREBL3, FIRETRT1, GRENADE1, GUN11, GUN13, EYESSIR1, GUN27,
				HEAL2, HYDROD1, INVUL2, KABOOM1, KABOOM12, KABOOM15, SPLASH9, KABOOM22, AACANON3, TANDETH1, MGUNINF1,
				MISSILE1, MISSILE6, MISSILE7, UNUSED1, PILLBOX1, RABEEP1, RAMENU1, SILENCER, TANK5, TANK6, TORPEDO1,
				TURRET1, TSLACHG2, TESLA1, SQUISHY2, SCOLDY1, RADARON2, RADARDN1, PLACBLDG, KABOOM30, KABOOM25,
				UNUSED2, DOGW7, DOGW3PX, CRMBLE2, CASHUP1, CASHDN1, BUILD5, BLEEP9, BLEEP6, BLEEP5, BLEEP17, BLEEP13,
				BLEEP12, BLEEP11, H2OBOMB2, CASHTURN, TUFFGUY1, ROKROLL1, LAUGH1, CMON1, BOMBIT1, GOTIT1, KEEPEM1,
				ONIT1, LEFTY1, YEAH1, YES1, YO1, WALLKIL2, UNUSED3, GUN5, SUBSHOW1, EINAH1, EINOK1, EINYES1, MINE1,
				SCOMND1, SYESSIR1, SINDEED1, SONWAY1, SKING1, MRESPON1, MYESSIR1, MAFFIRM1, MMOVOUT1, BEEPSLCT, SYEAH1,
				UNUSED4, UNUSED5, SMOUT1, SOKAY1, UNUSED6, SWHAT1, SAFFIRM1, STAVCMDR, STAVCRSE, STAVYES, STAVMOV,
				BUZY1, RAMBO1, RAMBO2, RAMBO3
			}

			enum MusicType
			{
				BIGF226M, CRUS226M, FAC1226M, FAC2226M, HELL226M, RUN1226M, SMSH226M, TREN226M, WORK226M, DENSE_R,
				FOGGER1A, MUD1A, RADIO2, ROLLOUT, SNAKE, TERMINAT, TWIN, VERTOR1A, MAP, SCORE, INTRO, CREDITS,
				_2ND_HAND, ARAZOID, BACKSTAB, CHAOS2, SHUT_IT, TWINMIX1, UNDER3, VR2
			}

			enum SpeechType
			{
				MISNWON1, MISNLST1, PROGRES1, CONSCMP1, UNITRDY1, NEWOPT1, NODEPLY1, STRCKIL1, NOPOWR1, NOFUNDS1, BCT1,
				REINFOR1, CANCLD1, ABLDGIN1, LOPOWER1, NOFUNDS1_, BASEATK1, NOBUILD1, PRIBLDG1, UNUSED1, UNUSED2,
				UNITLST1, SLCTTGT1, ENMYAPP1, SILOND1, ONHOLD1, REPAIR1, UNUSED3, UNUSED4, AUNITL1, UNUSED5, AAPPRO1,
				AARRIVE1, UNUSED6, UNUSED7, BLDGINF1, CHROCHR1, CHRORDY1, CHROYES1, CMDCNTR1, CNTLDED1, CONVYAP1,
				CONVLST1, XPLOPLC1, CREDIT1, NAVYLST1, SATLNCH1, PULSE1, UNUSED8, SOVFAPP1, SOVREIN1, TRAIN1, AREADY1,
				ALAUNCH1, AARRIVN1, AARRIVS1, AARIVE1, AARRIVW1, _1OBJMET1, _2OBJMET1, _3OBJMET1, IRONCHG1, IRONRDY1,
				KOSYRES1, OBJNMET1, FLAREN1, FLARES1, FLAREE1, FLAREW1, SPYPLN1, TANYAF1, ARMORUP1, FIREPO1, UNITSPD1,
				MTIMEIN1, UNITFUL1, UNITREP1, _40MINR, _30MINR, _20MINR, _10MINR, _5MINR, _4MINR, _3MINR, _2MINR,
				_1MINR, TIMERNO1, UNITSLD1, TIMERGO1, TARGRES1, TARGFRE1, TANYAR1, STRUSLD1, SOVFORC1, SOVEMP1,
				SOVEFAL1, OPTERM1, OBJRCH1, OBJNRCH1, OBJMET1, MERCR1, MERCF1, KOSYFRE1, FLARE1, COMNDOR1, COMNDOF1,
				BLDGPRG1, ATPREP1, ASELECT1, APREP1, ATLNCH1, AFALLEN1, AAVAIL1, AARRIVE1_, SAVE1, LOAD1
			}

			enum SpecialWeaponType
			{
				SONAR_PULSE, NUCLEAR_BOMB, CHRONOSPHERE, PARA_BOMB,
				PARA_INFANTRY, SPY_MISSION, IRON_CURTAIN, GPS
			}

			enum QuarryType
			{
				NONE, ANYTHING, BUILDINGS, HARVESTERS, INFANTRY, VEHICLES,
				VESSELS, FACTORIES, DEFENSE, THREAT, POWER, FAKES
			}

			readonly ActionType type;
			readonly int trigger;
			readonly int team;
			readonly int data;

			public Action(string typeToken, string teamToken, string triggerToken, string dataToken)
			{
				type = FieldLoader.GetValue<ActionType>("type", typeToken);
				team = FieldLoader.GetValue<int>("team", teamToken);
				trigger = FieldLoader.GetValue<int>("trigger", triggerToken);
				data = FieldLoader.GetValue<int>("data", dataToken);

				// Fix weirdly formatted data caused by union usage in the original game
				if (type != ActionType.TEXT_TRIGGER)
					data = data & 0xFF;
			}

			static string GetTeamName(int team, List<TeamType> teamTypes)
			{
				if (team < 0 || team >= teamTypes.Count)
					return "Unknown (ID {0})".F(team);

				return teamTypes[team].Name;
			}

			public string ToString(HouseType house, List<Trigger> allTriggers, Dictionary<int, CPos> waypoints, List<TeamType> teamTypes)
			{
				switch (type)
				{
					case ActionType.NONE: return null;
					case ActionType.WIN: return "House \"{0}\" is victorious".F((HouseType)data);
					case ActionType.LOSE: return "House \"{0}\" is defeated".F((HouseType)data);
					case ActionType.BEGIN_PRODUCTION: return "Enable production for house \"{0}\"".F((HouseType)data);
					case ActionType.CREATE_TEAM: return "Create team \"{0}\"".F(GetTeamName(team, teamTypes));
					case ActionType.DESTROY_TEAM: return "Disband team \"{0}\"".F(GetTeamName(team, teamTypes));
					case ActionType.ALL_HUNT: return "All units owned by house \"{0}\" hunt for enemies".F((HouseType)data);
					case ActionType.REINFORCEMENTS: return "Reinforce with team \"{0}\"".F(GetTeamName(team, teamTypes));
					case ActionType.DZ: return "Spawn flare with small vision radius at \"waypoint{0}\" ({1})".F(data, waypoints[data]);
					case ActionType.FIRE_SALE: return "House \"{0}\" sells all buildings".F((HouseType)data);
					case ActionType.PLAY_MOVIE: return "Play Movie {0}".F((MovieType)data);
					case ActionType.TEXT_TRIGGER: return "Display text message {0} from tutorial.ini".F(data);
					case ActionType.DESTROY_TRIGGER: return "Disable trigger \"{0}\"".F(allTriggers[trigger].Name);
					case ActionType.AUTOCREATE: return "Enable AI autocreate for house \"{0}\"".F((HouseType)data);
					case ActionType.WINLOSE: return "~don't use~ (does not work!)";
					case ActionType.ALLOWWIN: return "Allow player to win";
					case ActionType.REVEAL_ALL: return "Reveal entire map to player";
					case ActionType.REVEAL_SOME: return "Reveal area around \"waypoint{0}\" ({1})".F(data, waypoints[data]);
					case ActionType.REVEAL_ZONE: return "Reveal zone of \"waypoint{0}\" ({1})".F(data, waypoints[data]);
					case ActionType.PLAY_SOUND: return "Play sound effect \"{0}\"".F((SoundType)data);
					case ActionType.PLAY_MUSIC: return "Play music track \"{0}\"".F((MusicType)data);
					case ActionType.PLAY_SPEECH: return "Play speech message \"{0}\"".F((SpeechType)data);
					case ActionType.FORCE_TRIGGER: return "Run trigger \"{0}\"".F(allTriggers[trigger].Name);
					case ActionType.START_TIMER: return "Start mission timer";
					case ActionType.STOP_TIMER: return "Stop mission timer";
					case ActionType.ADD_TIMER: return "Add {0:F1} minutes to the mission timer".F(data / 10f);
					case ActionType.SUB_TIMER: return "Subtract {0:F1} minutes from the mission timer".F(data / 10f);
					case ActionType.SET_TIMER: return "Start mission timer with {0:F1} minutes remaining".F(data / 10f);
					case ActionType.SET_GLOBAL: return "Set global scenario variable {0}".F(data);
					case ActionType.CLEAR_GLOBAL: return "Clear global scenario variable {0}".F(data);
					case ActionType.BASE_BUILDING: return "Enable automatic base building for house \"{0}\"".F((HouseType)data);
					case ActionType.CREEP_SHADOW: return "Grow shroud one step";
					case ActionType.DESTROY_OBJECT: return "Destroy attached buildings";
					case ActionType.ONE_SPECIAL: return "Enable one-shot superweapon \"{0}\" for house \"{1}\"".F((SpecialWeaponType)data, house);
					case ActionType.FULL_SPECIAL: return "Enable repeating superweapon \"{0}\" for house \"{1}\"".F((SpecialWeaponType)data, house);
					case ActionType.PREFERRED_TARGET: return "Set preferred target for house \"{0}\" to  \"{1}\"".F(house, (QuarryType)data);
					case ActionType.LAUNCH_NUKES: return "Animate A-bomb launch";
					default:
						return "Action {0} Team: {1} Trigger: {2} Data: {3}".F(type, team, trigger, data);
				}
			}
		}

		public class Trigger
		{
			enum TriggerPersistantType { Volatile, SemiPersistant, Persistant }
			enum TriggerMultiStyleType { Only, And, Or, Linked }

			public readonly string Name;
			readonly TriggerPersistantType persistantType;
			readonly HouseType house;
			readonly TriggerMultiStyleType eventControl;
			readonly Event event1;
			readonly Event event2;
			readonly Action action1;
			readonly Action action2;

			public Trigger(string key, string value)
			{
				var tokens = value.Split(',');
				if (tokens.Length != 18)
					throw new InvalidDataException("Trigger {0} does not have 18 tokens.".F(key));

				Name = key;
				persistantType = (TriggerPersistantType)int.Parse(tokens[0]);
				house = FieldLoader.GetValue<HouseType>("House", tokens[1]);
				eventControl = (TriggerMultiStyleType)int.Parse(tokens[2]);
				event1 = new Event(tokens[4], tokens[5], tokens[6]);
				event2 = new Event(tokens[7], tokens[8], tokens[9]);
				action1 = new Action(tokens[10], tokens[11], tokens[12], tokens[13]);
				action2 = new Action(tokens[14], tokens[15], tokens[16], tokens[17]);
			}

			public MiniYaml Serialize(List<Trigger> triggers, Dictionary<string, List<CPos>> cellTriggers, Dictionary<int, CPos> waypoints, List<TeamType> teamTypes, Dictionary<string, List<string>> actorsByTrigger)
			{
				var yaml = new MiniYaml("");

				var eventNode = new MiniYamlNode("On", "");
				yaml.Nodes.Add(eventNode);
				eventNode.Value.Nodes.Add(new MiniYamlNode("", event1.ToString(house, teamTypes) ?? "Manual Trigger"));
				if (eventControl != TriggerMultiStyleType.Only)
				{
					var type = eventControl == TriggerMultiStyleType.And ? "AND" :
						eventControl == TriggerMultiStyleType.Or ? "OR" : "LINKED";

					eventNode.Value.Nodes.Add(new MiniYamlNode("", type));
					eventNode.Value.Nodes.Add(new MiniYamlNode("", event2.ToString(house, teamTypes)));
				}

				var actionsNode = new MiniYamlNode("Actions", "");
				yaml.Nodes.Add(actionsNode);

				var action1Label = action1.ToString(house, triggers, waypoints, teamTypes);
				if (action1Label != null)
					actionsNode.Value.Nodes.Add(new MiniYamlNode("", action1Label));

				var action2Label = action2.ToString(house, triggers, waypoints, teamTypes);
				if (action2Label != null)
					actionsNode.Value.Nodes.Add(new MiniYamlNode("", action2Label));

				var expires = persistantType == TriggerPersistantType.Persistant ? "Never" :
					persistantType == TriggerPersistantType.SemiPersistant ? "After running on all attached objects" :
					"After running once";
				yaml.Nodes.Add(new MiniYamlNode("Disable", expires));

				var attachedToNode = new MiniYamlNode("AttachedTo", "");
				if (cellTriggers.TryGetValue(Name, out var cells))
					attachedToNode.Value.Nodes.Add(new MiniYamlNode("Cells", FieldSaver.FormatValue(cells)));

				foreach (var t in teamTypes)
					if (t.Trigger != -1 && triggers[t.Trigger] == this)
						attachedToNode.Value.Nodes.Add(new MiniYamlNode("", "TeamType \"{0}\"".F(t.Name)));

				if (actorsByTrigger.TryGetValue(Name, out var actors))
					foreach (var a in actors)
						attachedToNode.Value.Nodes.Add(new MiniYamlNode("", a));

				if (attachedToNode.Value.Nodes.Any())
					yaml.Nodes.Add(attachedToNode);

				return yaml;
			}
		}

		public class TeamType
		{
			enum MissionType
			{
				SLEEP, ATTACK, MOVE, QMOVE, RETREAT, GUARD, STICKY, ENTER, CAPTURE, HARVEST, AREA_GUARD, RETURN,
				STOP, AMBUSH, HUNT, UNLOAD, SABOTAGE, CONSTRUCTION, SELLING, REPAIR, RESCUE, MISSILE, HARMLESS
			}

			public readonly string Name;
			public readonly int Trigger;
			readonly HouseType house;
			readonly bool isRoundAbout;
			readonly bool isSuicide;
			readonly bool isAutocreate;
			readonly bool isPrebuilt;
			readonly bool isReinforcable;
			readonly List<string> units = new List<string>();
			readonly List<(MissionType Type, int Data)> mission = new List<(MissionType, int)>();

			readonly int recruitPriority;
			readonly int initNum;
			readonly int maxAllowed;
			readonly int origin;

			public TeamType(string key, string value)
			{
				Name = key;

				var tokens = value.Split(',');
				house = FieldLoader.GetValue<HouseType>("House", tokens[0]);

				var flags = int.Parse(tokens[1]);
				isRoundAbout = (flags & 0x01) != 0;
				isSuicide = (flags & 0x02) != 0;
				isAutocreate = (flags & 0x04) != 0;
				isPrebuilt = (flags & 0x08) != 0;
				isReinforcable = (flags & 0x10) != 0;

				recruitPriority = int.Parse(tokens[2]);
				initNum = byte.Parse(tokens[3]);
				maxAllowed = byte.Parse(tokens[4]);
				origin = int.Parse(tokens[5]) + 1;
				Trigger = int.Parse(tokens[6]);

				var numClasses = int.Parse(tokens[7]);
				for (int i = 0; i < numClasses; i++)
				{
					var classTokens = tokens[8 + i].Split(':');
					var count = FieldLoader.GetValue<int>("token", classTokens[1]);
					for (var j = 0; j < count; j++)
						units.Add(classTokens[0]);
				}

				var numMissions = int.Parse(tokens[8 + numClasses]);
				for (int i = 0; i < numMissions; i++)
				{
					var missionTokens = tokens[9 + numClasses + i].Split(':');
					mission.Add((
						FieldLoader.GetValue<MissionType>("Type", missionTokens[0]),
						FieldLoader.GetValue<int>("Data", missionTokens[1])));
				}
			}

			public MiniYaml Serialize(List<Trigger> triggers, Dictionary<string, List<CPos>> cellTriggers, Dictionary<int, CPos> waypoints, List<TeamType> teamTypes, Dictionary<string, List<string>> actorsByTrigger)
			{
				var yaml = new MiniYaml("");
				yaml.Nodes.Add(new MiniYamlNode("Units", FieldSaver.FormatValue(units)));
				if (Trigger != -1)
					yaml.Nodes.Add(new MiniYamlNode("Trigger", triggers[Trigger].Name));

				return yaml;
			}
		}

		string IUtilityCommand.Name { get { return "--decode-ra-mission"; } }
		bool IUtilityCommand.ValidateArguments(string[] args)
		{
			return args.Length >= 2;
		}

		static CPos ParseCell(string token)
		{
			var cellNumber = FieldLoader.GetValue<int>("value", token);
			return new CPos(cellNumber % 128, cellNumber / 128);
		}

		[Desc("FILENAME", "Describe the triggers and teamptypes from a legacy Red Alert INI/MPR map.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			// HACK: The engine code assumes that Game.modData is set.
			Game.ModData = utility.ModData;

			var filename = args[1];
			using (var stream = File.OpenRead(filename))
			{
				var file = new IniFile(stream);
				var waypoints = new Dictionary<int, CPos>();
				var cellTriggers = new Dictionary<string, List<CPos>>();
				var triggers = new List<Trigger>();
				var teamTypes = new List<TeamType>();

				foreach (var t in file.GetSection("Waypoints"))
				{
					var waypointNumber = FieldLoader.GetValue<int>("key", t.Key);
					var cellNumber = FieldLoader.GetValue<int>("value", t.Value);
					waypoints[waypointNumber] = new CPos(cellNumber % 128, cellNumber / 128);
				}

				foreach (var t in file.GetSection("CellTriggers"))
				{
					var cellNumber = FieldLoader.GetValue<int>("key", t.Key);
					cellTriggers.GetOrAdd(t.Value).Add(new CPos(cellNumber % 128, cellNumber / 128));
				}

				foreach (var t in file.GetSection("Trigs"))
					triggers.Add(new Trigger(t.Key, t.Value));

				foreach (var t in file.GetSection("TeamTypes"))
					teamTypes.Add(new TeamType(t.Key, t.Value));

				// NOTE: Must be kept in sync with ImportLegacyMapCommand for Actor* names to match
				var actorsByTrigger = new Dictionary<string, List<string>>();
				var actorCount = 0;
				foreach (var section in new[] { "STRUCTURES", "UNITS", "INFANTRY", "SHIPS" })
				{
					foreach (var s in file.GetSection(section, true))
					{
						try
						{
							var parts = s.Value.Split(',');
							var owner = parts[0];
							var actorType = parts[1].ToLowerInvariant();
							var location = ParseCell(parts[3]);
							var trigger = parts[section == "STRUCTURES" ? 5 : parts.Length - 1];

							if (!Game.ModData.DefaultRules.Actors.ContainsKey(actorType))
								Console.WriteLine("Ignoring unknown actor type: `{0}`".F(parts[1].ToLowerInvariant()));
							else
							{
								if (trigger != "None")
									actorsByTrigger.GetOrAdd(trigger).Add("Actor{0} ({1} owned by house \"{2}\" at cell {3})".F(actorCount, actorType, owner, location));

								actorCount += 1;
							}
						}
						catch (Exception)
						{
							Console.WriteLine("Malformed actor definition: `{0}`".F(s));
						}
					}
				}

				foreach (var t in triggers)
					foreach (var l in t.Serialize(triggers, cellTriggers, waypoints, teamTypes, actorsByTrigger).ToLines("Trigger@" + t.Name))
						Console.WriteLine(l);

				foreach (var t in teamTypes)
					foreach (var l in t.Serialize(triggers, cellTriggers, waypoints, teamTypes, actorsByTrigger).ToLines("TeamType@" + t.Name))
						Console.WriteLine(l);

				var baseHouse = "Unknown";
				foreach (var s in file.GetSection("Base"))
				{
					if (s.Key == "Player")
					{
						baseHouse = s.Value;
						continue;
					}

					if (s.Key == "Count" && s.Value != "0")
					{
						Console.WriteLine("ScriptedBuildingProduction: house \"{0}\"", baseHouse);
						continue;
					}

					var parts = s.Value.Split(',');
					Console.WriteLine("\t{0} at cell {1}", parts[0], ParseCell(parts[1]));
				}
			}
		}
	}
}
