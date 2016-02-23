﻿using System;
using System.Collections.Generic;
using System.Linq;

using RimWorld;
using Verse;
using Verse.AI;

namespace RA
{
    public class WorkGiver_HaulToBurner : WorkGiver
    {
        IEnumerable<Thing> targetBurners;

        public override bool ShouldSkip(Pawn pawn)
        {
            targetBurners = UnreservedBurnersToRefill(pawn);
            return targetBurners.Count() == 0;
        }

        public override Job NonScanJob(Pawn pawn)
        {
            Thing closestFuel;
            int numToCarry;

            IEnumerable<Thing> allFuel = Find.ListerThings.ThingsInGroup(ThingRequestGroup.HaulableEver).Where(thing => thing.GetStatValue(StatDef.Named("BurnDurationSingleUnit")) > 0f && HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, thing));

            if (allFuel.Count() > 0)
            {

                Building_WorkTable_Fueled closestBurner = ClosestBurnerToRefill(pawn);
                // if burner is empty, any closest fuel type will do
                if (closestBurner.fuelContainer.Count == 0)
                {
                    closestFuel = GenClosest.ClosestThing_Global_Reachable(pawn.Position, allFuel, PathEndMode.ClosestTouch, TraverseParms.For(pawn, DangerUtility.NormalMaxDanger(pawn)));
                    numToCarry = closestFuel.def.stackLimit;
                }
                // else, only closest fuel type of the same def will do
                else
                {
                    closestFuel = GenClosest.ClosestThing_Global_Reachable(pawn.Position, allFuel, PathEndMode.ClosestTouch, TraverseParms.For(pawn, DangerUtility.NormalMaxDanger(pawn)), 9999, fuel => fuel.def == closestBurner.fuelContainer[0].def);
                    numToCarry = closestBurner.fuelContainer[0].def.stackLimit - closestBurner.fuelContainer[0].stackCount;
                }

                return new Job(DefDatabase<JobDef>.GetNamed("RefillBurner"), closestFuel, closestBurner)
                {
                    maxNumToCarry = numToCarry
                };
            }
            else
                return null;
        }
        
        public IEnumerable<Thing> UnreservedBurnersToRefill(Pawn pawn)
        {
            return Find.ListerBuildings.AllBuildingsColonistOfClass<Building_WorkTable_Fueled>().Where(burner => burner.RequireMoreFuel() && pawn.CanReserve(burner)).Cast<Thing>();
        }

        public Building_WorkTable_Fueled ClosestBurnerToRefill(Pawn pawn)
        {
            return (Building_WorkTable_Fueled)GenClosest.ClosestThing_Global_Reachable(pawn.Position, targetBurners, PathEndMode.Touch, TraverseParms.For(pawn, DangerUtility.NormalMaxDanger(pawn)));
        }
    }
}