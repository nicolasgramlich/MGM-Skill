﻿using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace WaynGroup.Mgm.Skill
{
    /// <summary>
    /// System that fake the user input simulating a skill cast every frame.
    /// </summary>
    [UpdateBefore(typeof(SkillUpdateSystem))]
    public class UserInputSimulationSystem : SystemBase
    {

        protected override void OnUpdate()
        {
            Dependency = Entities.ForEach((ref DynamicBuffer<SkillBuffer> skillBuffer) =>
            {
                NativeArray<SkillBuffer> sbArray = skillBuffer.AsNativeArray();
                for (int i = 0; i < sbArray.Length; i++)
                {
                    Skill Skill = sbArray[i];
                    Skill.TryCast();
                    sbArray[i] = Skill;
                }


            }).WithBurst()
            .ScheduleParallel(Dependency);
        }
    }
}