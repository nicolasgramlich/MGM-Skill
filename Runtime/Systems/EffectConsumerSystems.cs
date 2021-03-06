﻿
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace WaynGroup.Mgm.Skill
{



    public interface IEffectContext<EFFECT> where EFFECT : struct, IEffect
    {
        Entity Target { get; set; }
        EFFECT Effect { get; set; }
    }


    public abstract class EffectConsumerSystem<EFFECT, EFFECT_CTX> : SystemBase where EFFECT : struct, IEffect
        where EFFECT_CTX : struct, IEffectContext<EFFECT>
    {

        /// <summary>
        ///  The stream to Read/Write the contextualized effect. 
        /// </summary>
        private NativeStream EffectStream;

        /// <summary>
        /// A map o effect per targeted entity to improve consumer job performance.
        /// </summary>
        protected NativeMultiHashMap<Entity, EFFECT_CTX> Effects;

        /// <summary>
        /// The trigger job handle to make sure we finished trigerring all necessary effect before consuming the effects.
        /// </summary>
        private JobHandle TriggerJobHandle;

        protected override void OnCreate()
        {
            base.OnCreate();

            // Allocate the map only on create to avoid allocating every frame.
            Effects = new NativeMultiHashMap<Entity, EFFECT_CTX>(0, Allocator.Persistent);
        }


        /// <summary>
        /// Setup the dependecy between the trigger job and the consumer job to make sure we finished trigerring all necessary effect before consuming the effects.
        /// </summary>
        /// <param name="triggerJobHandle">The trigger job JobHandle.</param>
        public void RegisterTriggerDependency(JobHandle triggerJobHandle)
        {
            TriggerJobHandle = triggerJobHandle;
        }

        /// <summary>
        /// Get a NativeStream.Writer to write the effects to consume. 
        /// </summary>
        /// <param name="foreachCount">The number of chunk of thread that writes to the NativeStream</param>
        /// <returns></returns>
        public NativeStream.Writer GetConsumerWriter(int foreachCount)
        {
            EffectStream = new NativeStream(foreachCount, Allocator.TempJob);
            return EffectStream.AsWriter();
        }


        protected override void OnDestroy()
        {
            base.OnDestroy();

            if (EffectStream.IsCreated)
            {
                EffectStream.Dispose(Dependency);
            }
            if (Effects.IsCreated)
            {
                Effects.Dispose(Dependency);
            }
        }

        /// <summary>
        /// Delegate the effect consumption logic to the derived class.
        /// </summary>
        protected abstract void Consume();

        /// <summary>
        /// This job reads all the effects to apply and dsipatche them into a map by targeted entity.
        /// This ensures better performance overall in consuming the effect.
        /// </summary>
        [BurstCompile]
        struct RemapEffects : IJobParallelFor
        {
            [ReadOnly] public NativeStream.Reader EffectReader;
            public NativeMultiHashMap<Entity, EFFECT_CTX>.ParallelWriter EffectsWriter;
            public void Execute(int index)
            {
                EffectReader.BeginForEachIndex(index);
                int rangeItemCount = EffectReader.RemainingItemCount;
                for (int j = 0; j < rangeItemCount; j++)
                {

                    EFFECT_CTX effect = EffectReader.Read<EFFECT_CTX>();
                    EffectsWriter.Add(effect.Target, effect);
                }

                EffectReader.EndForEachIndex();
            }
        }

        /// <summary>
        /// Clear the effect map and allocate additional capacity if needed.
        /// </summary>
        [BurstCompile]
        struct SetupEffectMap : IJob
        {
            [ReadOnly] public NativeStream.Reader EffectReader;
            public NativeMultiHashMap<Entity, EFFECT_CTX> Effects;
            public void Execute()
            {
                Effects.Clear();
                if (Effects.Capacity < EffectReader.ComputeItemCount())
                {
                    Effects.Capacity = EffectReader.ComputeItemCount();
                }
            }
        }

        protected sealed override void OnUpdate()
        {

            Dependency = JobHandle.CombineDependencies(Dependency, TriggerJobHandle);

            // If the producer did not actually write anything to the stream, the native stream will not be flaged as created.
            // In that case we don't need to do anything.
            // Not doing this checks actually result in a non authrorized access to the memory and crashes Unity.
            if (!EffectStream.IsCreated) return;

            NativeStream.Reader effectReader = EffectStream.AsReader();
            SetupEffectMap AllocateJob = new SetupEffectMap()
            {
                EffectReader = effectReader,
                Effects = Effects
            };
            Dependency = AllocateJob.Schedule(Dependency);


            NativeMultiHashMap<Entity, EFFECT_CTX>.ParallelWriter effectsWriter = Effects.AsParallelWriter();
            RemapEffects RemapEffectsJob = new RemapEffects()
            {
                EffectReader = effectReader,
                EffectsWriter = Effects.AsParallelWriter()
            };
            Dependency = RemapEffectsJob.Schedule(effectReader.ForEachCount, 1, Dependency);

            // Call the effect consumption logic defined in hte derived class.
            Consume();

            Dependency = EffectStream.Dispose(Dependency);
        }


    }
}
