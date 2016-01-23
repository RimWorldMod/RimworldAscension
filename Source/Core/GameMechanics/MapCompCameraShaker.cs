﻿using System.Collections.Generic;
using System;

using Verse;
using UnityEngine;
using RimWorld;

namespace RA
{
    class MapCompCameraShaker : MapComponent
    {
        public const float ShakeDecayRate = 0.5f;
        public const float ShakeFrequency = 24f;
        public const float MaxShakeMag = 1f;
        public static float curShakeMag = 0f;

        public static Vector3 ShakeOffset
        {
            get
            {
                float x = (float)Math.Sin((double)(Find.RealTime.timeUnpaused * ShakeFrequency)) * curShakeMag;
                float y = (float)Math.Sin((double)(Find.RealTime.timeUnpaused * ShakeFrequency) * 1.05) * curShakeMag;
                float z = (float)Math.Sin((double)(Find.RealTime.timeUnpaused * ShakeFrequency) * 1.1) * curShakeMag;
                return new Vector3(x, y, z);
            }
        }

        public static void DoShake(float mag)
        {
            curShakeMag += mag;
        }

        public override void MapComponentTick()
        {
            if (curShakeMag > 0)
            {
                if (curShakeMag > MaxShakeMag)
                    curShakeMag = MaxShakeMag;

                Find.CameraMap.JumpTo(Find.CameraMap.MapPosition.ToVector3Shifted() + ShakeOffset);
            }
        }

        public override void MapComponentUpdate()
        {
            if (curShakeMag > 0)
            {
                curShakeMag -= ShakeDecayRate * Time.deltaTime;

                if (curShakeMag < 0f)
                    curShakeMag = 0f;
            }
        }
    }
}