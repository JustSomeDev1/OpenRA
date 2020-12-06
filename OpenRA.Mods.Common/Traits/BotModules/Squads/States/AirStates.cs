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
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.BotModules.Squads
{
	abstract class AirStateBase : StateBase
	{
		static readonly BitSet<TargetableType> AirTargetTypes = new BitSet<TargetableType>("Air");

		protected const int MissileUnitMultiplier = 3;

		protected static int CountAntiAirUnits(IEnumerable<Actor> units)
		{
			if (!units.Any())
				return 0;

			var missileUnitsCount = 0;
			foreach (var unit in units)
			{
				if (unit == null || unit.Info.HasTraitInfo<AircraftInfo>())
					continue;

				foreach (var ab in unit.TraitsImplementing<AttackBase>())
				{
					if (ab.IsTraitDisabled || ab.IsTraitPaused)
						continue;

					foreach (var a in ab.Armaments)
					{
						if (a.Weapon.IsValidTarget(AirTargetTypes))
						{
							missileUnitsCount++;
							break;
						}
					}
				}
			}

			return missileUnitsCount;
		}

		protected static Actor FindDefenselessTarget(Squad squad)
		{
			Actor target = null;
			FindSafePlace(squad, out target, true);
			return target;
		}

		protected static CPos? FindSafePlace(Squad squad, out Actor detectedEnemyTarget, bool needTarget)
		{
			var map = squad.World.Map;
			var dangerRadius = squad.SquadManager.Info.DangerScanRadius;
			detectedEnemyTarget = null;

			var columnCount = (map.MapSize.X + dangerRadius - 1) / dangerRadius;
			var rowCount = (map.MapSize.Y + dangerRadius - 1) / dangerRadius;

			var checkIndices = Exts.MakeArray(columnCount * rowCount, i => i).Shuffle(squad.World.LocalRandom);
			foreach (var i in checkIndices)
			{
				var pos = new MPos((i % columnCount) * dangerRadius + dangerRadius / 2, (i / columnCount) * dangerRadius + dangerRadius / 2).ToCPos(map);

				if (NearToPosSafely(squad, map.CenterOfCell(pos), out detectedEnemyTarget))
				{
					if (needTarget && detectedEnemyTarget == null)
						continue;

					return pos;
				}
			}

			return null;
		}

		protected static bool NearToPosSafely(Squad squad, WPos loc)
		{
			return NearToPosSafely(squad, loc, out _);
		}

		protected static bool NearToPosSafely(Squad squad, WPos loc, out Actor detectedEnemyTarget)
		{
			detectedEnemyTarget = null;
			var dangerRadius = squad.SquadManager.Info.DangerScanRadius;
			var unitsAroundPos = squad.World.FindActorsInCircle(loc, WDist.FromCells(dangerRadius))
				.Where(squad.SquadManager.IsPreferredEnemyUnit).ToList();

			if (!unitsAroundPos.Any())
				return true;

			if (CountAntiAirUnits(unitsAroundPos) * MissileUnitMultiplier < squad.Units.Count)
			{
				detectedEnemyTarget = unitsAroundPos.Random(squad.Random);
				return true;
			}

			return false;
		}

		// Checks the number of anti air enemies around units
		protected virtual bool ShouldFlee(Squad squad)
		{
			return ShouldFlee(squad, enemies => CountAntiAirUnits(enemies) * MissileUnitMultiplier > squad.Units.Count);
		}
	}

	class AirIdleState : AirStateBase, IState
	{
		public void Activate(Squad squad) { }

		public void Tick(Squad squad)
		{
			if (!squad.IsValid)
				return;

			if (ShouldFlee(squad))
			{
				squad.FuzzyStateMachine.ChangeState(squad, new AirFleeState(), true);
				return;
			}

			var e = FindDefenselessTarget(squad);
			if (e == null)
				return;

			squad.Target = Target.FromActor(e);
			squad.FuzzyStateMachine.ChangeState(squad, new AirAttackState(), true);
		}

		public void Deactivate(Squad squad) { }
	}

	class AirAttackState : AirStateBase, IState
	{
		public void Activate(Squad squad) { }

		public void Tick(Squad squad)
		{
			if (!squad.IsValid)
				return;

			if (!squad.IsTargetValid)
			{
				var a = squad.Units.Random(squad.Random);
				var closestEnemy = squad.SquadManager.FindClosestEnemy(a.CenterPosition);
				if (closestEnemy != null)
					squad.Target = Target.FromActor(closestEnemy);
				else
				{
					squad.FuzzyStateMachine.ChangeState(squad, new AirFleeState(), true);
					return;
				}
			}

			if (!NearToPosSafely(squad, squad.Target.CenterPosition))
			{
				squad.FuzzyStateMachine.ChangeState(squad, new AirFleeState(), true);
				return;
			}

			foreach (var a in squad.Units)
			{
				if (BusyAttack(a))
					continue;

				var ammoPools = a.TraitsImplementing<AmmoPool>().ToArray();
				if (!ReloadsAutomatically(ammoPools, a.TraitOrDefault<Rearmable>()))
				{
					if (IsRearming(a))
						continue;

					if (!HasAmmo(ammoPools))
					{
						squad.Bot.QueueOrder(new Order("ReturnToBase", a, false));
						continue;
					}
				}

				if (CanAttackTarget(a, squad.Target))
					squad.Bot.QueueOrder(new Order("Attack", a, squad.Target, false));
			}
		}

		public void Deactivate(Squad squad) { }
	}

	class AirFleeState : AirStateBase, IState
	{
		public void Activate(Squad squad) { }

		public void Tick(Squad squad)
		{
			if (!squad.IsValid)
				return;

			foreach (var a in squad.Units)
			{
				var ammoPools = a.TraitsImplementing<AmmoPool>().ToArray();
				if (!ReloadsAutomatically(ammoPools, a.TraitOrDefault<Rearmable>()) && !FullAmmo(ammoPools))
				{
					if (IsRearming(a))
						continue;

					squad.Bot.QueueOrder(new Order("ReturnToBase", a, false));
					continue;
				}

				squad.Bot.QueueOrder(new Order("Move", a, Target.FromCell(squad.World, RandomBuildingLocation(squad)), false));
			}

			squad.FuzzyStateMachine.ChangeState(squad, new AirIdleState(), true);
		}

		public void Deactivate(Squad squad) { }
	}
}
