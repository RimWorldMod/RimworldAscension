﻿using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace RA
{
    public static class RA_Toils_Recipe
    {
        // added support for fuel burners
        public static Toil DoRecipeWork()
        {
            // research injection. initialized here to check if research project wan't change during work
            var startedResearch = Find.ResearchManager.currentProj;

            var toil = new Toil();
            toil.initAction = () =>
            {
                var actor = toil.actor;
                var curJob = actor.jobs.curJob;
                var curDriver = actor.jobs.curDriver as JobDriver_DoBill;

                var unfinishedThing = curJob.GetTarget(TargetIndex.B).Thing as UnfinishedThing;
                if (unfinishedThing != null && unfinishedThing.Initialized)
                {
                    curDriver.workLeft = unfinishedThing.workLeft;
                }
                else
                {
                    // research injection
                    var researchComp =
                        toil.actor.jobs.curJob.GetTarget(TargetIndex.A).Thing.TryGetComp<CompResearcher>();
                    if (researchComp != null)
                    {
                        curDriver.workLeft = startedResearch.CostApparent -
                                             startedResearch.ProgressApparent;
                    }
                    else
                    {
                        curDriver.workLeft = curJob.bill.recipe.WorkAmountTotal(unfinishedThing?.Stuff);
                    }

                    if (unfinishedThing != null)
                    {
                        unfinishedThing.workLeft = curDriver.workLeft;
                    }
                }
                curDriver.billStartTick = Find.TickManager.TicksGame;
                curJob.bill.Notify_DoBillStarted();
            };
            toil.tickAction = () =>
            {
                var actor = toil.actor;
                var curJob = actor.jobs.curJob;

                actor.GainComfortFromCellIfPossible();

                // burner support injection
                var burner = curJob.GetTarget(TargetIndex.A).Thing.TryGetComp<CompFueled>();
                if (burner != null && burner.internalTemp < burner.compFueled.Properties.operatingTemp)
                {
                    return;
                }

                var unfinishedThing = curJob.GetTarget(TargetIndex.B).Thing as UnfinishedThing;
                if (unfinishedThing != null && unfinishedThing.Destroyed)
                {
                    actor.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                curJob.bill.Notify_PawnDidWork(actor);

                var billGiverWithTickAction =
                    curJob.GetTarget(TargetIndex.A).Thing as IBillGiverWithTickAction;
                billGiverWithTickAction?.BillTick();

                if (curJob.RecipeDef.workSkill != null)
                {
                    actor.skills.GetSkill(curJob.RecipeDef.workSkill)
                        .Learn(LearnRates.XpPerTickRecipeBase*curJob.RecipeDef.workSkillLearnFactor);
                }

                var workProgress = (curJob.RecipeDef.workSpeedStat != null)
                    ? actor.GetStatValue(curJob.RecipeDef.workSpeedStat)
                    : 1f;

                var curDriver = actor.jobs.curDriver as JobDriver_DoBill;
                var building_WorkTable = curDriver.BillGiver as Building_WorkTable;
                var researchComp = curJob.GetTarget(TargetIndex.A).Thing.TryGetComp<CompResearcher>();
                if (building_WorkTable != null)
                {
                    // research injection
                    workProgress *= researchComp == null
                        ? building_WorkTable.GetStatValue(StatDefOf.WorkTableWorkSpeedFactor)
                        : building_WorkTable.GetStatValue(StatDefOf.ResearchSpeedFactor);
                }

                if (DebugSettings.fastCrafting)
                {
                    workProgress *= 30f;
                }

                curDriver.workLeft -= workProgress;
                if (unfinishedThing != null)
                {
                    unfinishedThing.workLeft = curDriver.workLeft;
                }

                // research injection
                if (researchComp != null)
                {
                    if (Find.ResearchManager.currentProj != null && Find.ResearchManager.currentProj == startedResearch)
                    {
                        Find.ResearchManager.ResearchPerformed(workProgress, actor);
                    }
                    if (Find.ResearchManager.currentProj != startedResearch)
                    {
                        actor.jobs.EndCurrentJob(JobCondition.Succeeded);
                        // scatter around all ingridients
                        foreach (var cell in building_WorkTable.IngredientStackCells)
                        {
                            var ingridientsOnCell =
                                Find.ThingGrid.ThingsListAtFast(cell)?
                                    .Where(thing => thing.def.category == ThingCategory.Item)
                                    .ToList();
                            if (!ingridientsOnCell.NullOrEmpty())
                            {
                                Thing dummy;
                                // despawn thing to spawn again with TryPlaceThing
                                ingridientsOnCell.FirstOrDefault().DeSpawn();
                                if (
                                    !GenPlace.TryPlaceThing(ingridientsOnCell.FirstOrDefault(),
                                        building_WorkTable.InteractionCell,
                                        ThingPlaceMode.Near, out dummy))
                                {
                                    Log.Error("No free spot for " + ingridientsOnCell);
                                }
                            }
                        }
                    }
                }
                else if (curDriver.workLeft <= 0f)
                {
                    curDriver.ReadyForNextToil();
                }

                if (curJob.bill.recipe.UsesUnfinishedThing)
                {
                    var num2 = Find.TickManager.TicksGame - curDriver.billStartTick;
                    if (num2 >= 3000 && num2%1000 == 0)
                    {
                        actor.jobs.CheckForJobOverride();
                    }
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Never;
            toil.WithEffect(() => toil.actor.CurJob.bill.recipe.effectWorking, TargetIndex.A);
            toil.PlaySustainerOrSound(() => toil.actor.CurJob.bill.recipe.soundWorking);
            toil.WithProgressBar(TargetIndex.A, () =>
            {
                var actor = toil.actor;
                var curJob = actor.CurJob;
                var unfinishedThing = curJob.GetTarget(TargetIndex.B).Thing as UnfinishedThing;
                var researchComp = curJob.GetTarget(TargetIndex.A).Thing.TryGetComp<CompResearcher>();
                // research injection
                return researchComp == null
                    ? 1f - ((JobDriver_DoBill) actor.jobs.curDriver).workLeft/
                      curJob.bill.recipe.WorkAmountTotal(unfinishedThing?.Stuff)
                    : startedResearch.ProgressPercent;
            });
            return toil;
        }

        // changed how product stuff type is determined and made this toil assign production cost for the Thing to the CompCraftedValue
        public static Toil FinishRecipeAndStartStoringProduct()
        {
            var toil = new Toil();
            toil.initAction = delegate
            {
                var actor = toil.actor;
                var curJob = actor.jobs.curJob;

                var jobDriver_DoBill = (JobDriver_DoBill)actor.jobs.curDriver;
                if (curJob.RecipeDef.workSkill != null && !curJob.RecipeDef.UsesUnfinishedThing)
                {
                    var xp = jobDriver_DoBill.ticksSpentDoingRecipeWork * 0.11f * curJob.RecipeDef.workSkillLearnFactor;
                    actor.skills.GetSkill(curJob.RecipeDef.workSkill).Learn(xp);
                }

                Thing dominantIngredient;
                var ingredients = ProcessedIngredients(curJob, out dominantIngredient);

                var products = GenRecipe.MakeRecipeProducts(curJob.RecipeDef, actor, ingredients, dominantIngredient).ToList();
                curJob.bill.Notify_IterationCompleted(actor, ingredients);
                RecordsUtility.Notify_BillDone(actor, products);
                
                // set production cost for all products
                foreach (var product in products)
                {
                    var compCraftedValue = product.TryGetComp<CompCraftedValue>();
                    compCraftedValue?.SetMarketValue(curJob.RecipeDef, ingredients);
                }

                if (products.Count == 0)
                {
                    actor.jobs.EndCurrentJob(JobCondition.Succeeded);
                    return;
                }
                if (curJob.bill.GetStoreMode() == BillStoreMode.DropOnFloor)
                {
                    foreach (var thing in products.Where(thing =>
                        !GenPlace.TryPlaceThing(thing, actor.Position, ThingPlaceMode.Near)))
                    {
                        Log.Error(string.Concat(actor, " could not drop recipe product ", thing, " near ",
                            actor.Position));
                    }
                    actor.jobs.EndCurrentJob(JobCondition.Succeeded);
                    return;
                }
                if (products.Count > 1)
                {
                    for (var j = 1; j < products.Count; j++)
                    {
                        if (!GenPlace.TryPlaceThing(products[j], actor.Position, ThingPlaceMode.Near))
                        {
                            Log.Error(string.Concat(actor, " could not drop recipe product ", products[j], " near ",
                                actor.Position));
                        }
                    }
                }
                products[0].SetPositionDirect(actor.Position);
                IntVec3 vec;
                if (StoreUtility.TryFindBestBetterStoreCellFor(products[0], actor, StoragePriority.Unstored,
                    actor.Faction,
                    out vec))
                {
                    actor.carrier.TryStartCarry(products[0]);
                    curJob.targetB = vec;
                    curJob.targetA = products[0];
                    curJob.maxNumToCarry = 99999;
                    return;
                }
                if (!GenPlace.TryPlaceThing(products[0], actor.Position, ThingPlaceMode.Near))
                {
                    Log.Error(string.Concat("Bill doer could not drop product ", products[0], " near ", actor.Position));
                }
                actor.jobs.EndCurrentJob(JobCondition.Succeeded);
            };
            return toil;
        }

        // hidden in vanilla
        public static Thing GetDominantIngredient(RecipeDef recipe, List<Thing> ingredients)
        {
            // checks if there are any stuff ingredients used which are not forbidden in defaultIngredientFilter
            if (!recipe.products.NullOrEmpty())
            {
                var stuffBasedProduct =
                    recipe.products.FirstOrDefault(product => product.thingDef.MadeFromStuff).thingDef;
                if (stuffBasedProduct != null)
                {
                    return ingredients.Find(
                        ingredient =>
                            ingredient.def.IsStuff
                            && ingredient.def.stuffProps.CanMake(stuffBasedProduct)
                            && (!recipe.defaultIngredientFilter?.Allows(ingredient.def) ?? true));
                }
            }
            return ingredients.MaxBy(product => product.stackCount);
        }

        // hidden in vanilla
        public static List<Thing> ProcessedIngredients(Job job, out Thing dominantIngredient)
        {
            var ingredients = new List<Thing>();
            var uft = job.GetTarget(TargetIndex.B).Thing as UnfinishedThing;
            if (uft != null)
            {
                dominantIngredient = uft.def.MadeFromStuff ? uft.ingredients.First(ing => ing.def == uft.Stuff) : null;
                ingredients = uft.ingredients;
                uft.Destroy();
                job.placedThings = null;
                return ingredients;
            }
            if (job.placedThings != null)
            {
                foreach (var thing in job.placedThings.Select(stack => stack.Split()))
                {
                    if (ingredients.Contains(thing))
                    {
                        Log.Error("Tried to add ingredient from job placed targets twice: " + thing);
                    }
                    else
                    {
                        ingredients.Add(thing);
                        var strippable = thing as IStrippable;
                        strippable?.Strip();
                        if (job.RecipeDef.UsesUnfinishedThing)
                        {
                            Find.DesignationManager.RemoveAllDesignationsOn(thing);
                            if (thing.Spawned)
                            {
                                thing.DeSpawn();
                            }
                        }
                        else
                        {
                            thing.Destroy();
                        }
                    }
                }
            }
            job.placedThings = null;
            dominantIngredient = ingredients.NullOrEmpty() ? null : GetDominantIngredient(job.RecipeDef, ingredients);
            return ingredients;
        }
    }
}
