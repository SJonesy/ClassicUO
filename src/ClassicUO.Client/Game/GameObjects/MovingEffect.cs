#region license

// Copyright (c) 2024, andreakarasho
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
// 1. Redistributions of source code must retain the above copyright
//    notice, this list of conditions and the following disclaimer.
// 2. Redistributions in binary form must reproduce the above copyright
//    notice, this list of conditions and the following disclaimer in the
//    documentation and/or other materials provided with the distribution.
// 3. All advertising materials mentioning features or use of this software
//    must display the following acknowledgement:
//    This product includes software developed by andreakarasho - https://github.com/andreakarasho
// 4. Neither the name of the copyright holder nor the
//    names of its contributors may be used to endorse or promote products
//    derived from this software without specific prior written permission.
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS ''AS IS'' AND ANY
// EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
// WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER BE LIABLE FOR ANY
// DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
// (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
// LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
// ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
// SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

#endregion

using System;
using System.IO;
using ClassicUO.Assets;
using ClassicUO.Game.Data;
using ClassicUO.Game.Managers;
using ClassicUO.Utility.Logging;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.GameObjects
{
    internal sealed class MovingEffect : GameEffect
    {        
        public MovingEffect
        (
            World world,
            EffectManager manager,
            uint src,
            uint trg,
            ushort xSource,
            ushort ySource,
            sbyte zSource,
            ushort xTarget,
            ushort yTarget,
            sbyte zTarget,
            ushort graphic,
            ushort hue,
            bool fixedDir,
            int duration,
            byte speed
        ) : base(world, manager, graphic, hue, 0, speed)
        {
            FixedDir = fixedDir;

            // we override interval time with speed
            var d = Constants.ITEM_EFFECT_ANIMATION_DELAY * 2;

            IntervalInMs = (uint)(d + (speed * d));

            // moving effects want a +22 to the X
            Offset.X += 22;

            Entity source = World.Get(src);
            if (SerialHelper.IsValid(src) && source != null)
            {
                SetSource(source);
            }
            else
            {
                SetSource(xSource, ySource, zSource);
            }

            Entity target = World.Get(trg);
            if (SerialHelper.IsValid(trg) && target != null)
            {
                SetTarget(target);
            }
            else
            {
                SetTarget(xTarget, yTarget, zTarget);
            }

            MovingEffectEndTime = duration > 0 ? Time.Ticks + (duration * 100) : -1f;
            LastTimeInTicks = Time.Ticks;
        }

        public readonly bool FixedDir;

        public float MovingEffectEndTime = 0f;
        private float LastTimeInTicks = 0f;

        public override void Update()
        {
            base.Update();

            if (MovingEffectEndTime > 0f)
            {
                MoveBasedOnDurationV2();
            }
            else
            {
                MoveBasedOnSpeed();
            }
        }

        private int GetCharacterHeightOffset(Mobile m)
        {
            int yOffset = 0;
            if (m.IsMounted)
            {
                Item mount = m.FindItemByLayer(Layer.Mount);
                if (mount != null)
                {
                    ushort model = mount.GetGraphicForAnimation();
                    /*
                    if (model != 0xFFFF && model < Constants.MAX_ANIMATIONS_DATA_INDEX_COUNT)
                    {
                        yOffset += AnimationsLoader.Instance.GetMountedHeightOffset(model) + Constants.DEFAULT_CHARACTER_HEIGHT;
                    }
                    */
                }
            }
            else
            {
                yOffset += Constants.DEFAULT_CHARACTER_HEIGHT / 2;
            }

            return yOffset;
        }

        private void MoveBasedOnDurationV2()
        {
            uint CurrentTick = Time.Ticks;

            if (IsDestroyed || !IsEnabled)
            {
                return;
            }

            if (MovingEffectEndTime <= CurrentTick)
            {
                RemoveMe();
                return;
            }

            // Set the Source
            Vector3 source = new Vector3(X * 22, Y * 22, Z * 4);
            source.X += (Offset.X + Offset.Y) / 2.0f;
            source.Y += (Offset.Y - Offset.X) / 2.0f;
            source.Z += Offset.Z;

            // Set the Target
            Vector3 target;
            if (Target != null)
            {
                if (Target is Mobile m)
                    target = new Vector3(Target.X * 22, Target.Y * 22, (Target.Z + 8) * 4);
                else
                    target = new Vector3(Target.X * 22, Target.Y * 22, Target.Z * 4);
                target.X += (Target.Offset.X + Target.Offset.Y) / 2.0f;
                target.Y += (Target.Offset.Y - Target.Offset.X) / 2.0f;
                target.Z += Target.Offset.Z;
            }
            else
            {
                target = new Vector3(TargetX * 22, TargetY * 22, TargetZ * 4);
            }
            /*
            // Determine how far we should move this update
            float timeSinceLastUpdate = CurrentTick - LastTimeInTicks;
            float timeRemaining = MovingEffectEndTime - CurrentTick;
            float percentToTravelThisUpdate = timeSinceLastUpdate / timeRemaining;

            // Get the next point
            Vector3 offsetToNextPosition = target - source;
            Vector3 newPosition = source + (offsetToNextPosition * percentToTravelThisUpdate);
            */

            Vector3.Subtract(ref target, ref source, out Vector3 path);

            var speed = (path.Length() / (MovingEffectEndTime - LastTimeInTicks));

            path.Normalize();
            Vector3.Multiply(ref path, speed * (CurrentTick - LastTimeInTicks), out path);
            Vector3.Add(ref source, ref path, out Vector3 newPosition);

            ushort newX = (ushort)(newPosition.X / 22);
            ushort newY = (ushort)(newPosition.Y / 22);
            sbyte newZ = (sbyte)(newPosition.Z / 4);

            // Update the effect's real location (for layering/depth calculations)
            if (newX != X || newY != Y || newZ != Z)
            {
                SetInWorldTile(newX, newY, newZ);
            }

            // Update the offset
            Offset.X = newPosition.X % 22 - newPosition.Y % 22;
            Offset.Y = newPosition.X % 22 + newPosition.Y % 22;
            Offset.Z = newPosition.Z % 4;

            // Update the angle
            AngleToTarget = GetEffectAngle();

            // Set persistent values for next Update()
            LastTimeInTicks = CurrentTick;

            Log.Trace($"Updated MovingEffect: ({CurrentTick}) Current: {X}x {Y}y {Z}z | Target: {Target.X}x {Target.Y}y {Target.Z}z | Offset: {Offset.X}x {Offset.Y}y {Offset.Z}z");
        }

        private void MoveBasedOnDuration()
        {
            uint CurrentTick = Time.Ticks;

            if (MovingEffectEndTime <= CurrentTick)
            {
                RemoveMe();
                return;
            }

            // Update the Target if it's alive and has moved
            if (Target != null && !Target.IsDestroyed && (Target.X != TargetX || Target.Y != TargetY || TargetZ != Target.Z))
            {
                TargetX = Target.X;
                TargetY = Target.Y;
                TargetZ = Target.Z;
            }

            // Initialize SourceScreenPosition, CurrentScreenPosition, and TargetScreenPosition
            int offsetSourceX = (int)(X - World.Player.X);
            int offsetSourceY = (int)(Y - World.Player.Y);
            int offsetSourceZ = (int)(Z - World.Player.Z);
            Vector2 currentScreenPosition = new Vector2((offsetSourceX - offsetSourceY) * 22, (offsetSourceX + offsetSourceY) * 22 - offsetSourceZ * 4);
            int offsetTargetX = TargetX - World.Player.X;
            int offsetTargetY = TargetY - World.Player.Y;
            int offsetTargetZ = TargetZ - World.Player.Z;
            Vector2 targetScreenPosition = new Vector2((offsetTargetX - offsetTargetY) * 22, (offsetTargetX + offsetTargetY) * 22 - offsetTargetZ * 4);

            // Determine how far we should move this update
            float timeSinceLastUpdate = CurrentTick - LastTimeInTicks;
            float timeRemaining = MovingEffectEndTime - CurrentTick;
            float percentToTravelThisUpdate = timeSinceLastUpdate / timeRemaining;

            // Calculate the next point (https://math.stackexchange.com/questions/333350/moving-point-along-the-vector)
            Vector2 offsetToNextPosition = targetScreenPosition - currentScreenPosition;
            Vector2 nextScreenPosition = currentScreenPosition + (offsetToNextPosition * percentToTravelThisUpdate);

            // Offset is the actual screen position to draw the effect at, relative to the origin at 0,0
            // Adding TILE_SIZE to X makes the effect start from the center of the sender and end in the center of the target
            Offset.X = nextScreenPosition.X - currentScreenPosition.X + 22;
            Offset.Y = nextScreenPosition.Y - currentScreenPosition.Y;

            // Set the effect angle to the target
            AngleToTarget = GetEffectAngle();

            // Move the source 
            Vector3 source = new Vector3(X * 22, Y * 22, Z * 4);
            source.X += (Offset.X + Offset.Y) / 2.0f;
            source.Y += (Offset.Y - Offset.X) / 2.0f;
            source.Z += Offset.Z;
            Vector3 path = new Vector3(offsetToNextPosition.X, offsetToNextPosition.Y, TargetZ);
            Vector3.Add(ref source, ref path, out Vector3 newPosition);
            ushort newX = (ushort)(newPosition.X / 22);
            ushort newY = (ushort)(newPosition.Y / 22);
            sbyte newZ = (sbyte)(newPosition.Z / 4);
            if (newX != X || newY != Y || newZ != Z)
            {
                SetInWorldTile(newX, newY, newZ);
            }

            // Set persistent values for next Update()
            LastTimeInTicks = CurrentTick;

            Log.Trace($"Updated MovingEffect: ({CurrentTick}) CurrentScreenPosition: {currentScreenPosition.X}x {currentScreenPosition.Y}y | Offset: {Offset.X}x {Offset.Y}y | TargetScreenPosition: {targetScreenPosition.X}x {targetScreenPosition.Y}y");
        }

        // This was copied from MoveBasedOnSpeed(). 
        private float GetEffectAngle()
        {
            (int sX, int sY, int sZ) = GetSource();
            int offsetSourceX = sX - World.Player.X;
            int offsetSourceY = sY - World.Player.Y;
            int offsetSourceZ = sZ - World.Player.Z;

            (int tX, int tY, int tZ) = GetTarget();
            int offsetTargetX = tX - World.Player.X;
            int offsetTargetY = tY - World.Player.Y;
            int offsetTargetZ = tZ - World.Player.Z;

            Vector2 source = new Vector2((offsetSourceX - offsetSourceY) * 22, (offsetSourceX + offsetSourceY) * 22 - offsetSourceZ * 4);
            source.X += Offset.X;
            source.Y += Offset.Y;
            Vector2 target = new Vector2((offsetTargetX - offsetTargetY) * 22, (offsetTargetX + offsetTargetY) * 22 - offsetTargetZ * 4);

            var offset = target - source;
            var distance = offset.Length();
            var frameIndependentSpeed = IntervalInMs * Time.Delta;

            if (distance > frameIndependentSpeed)
            {
                offset.Normalize();
            }

            return (float)Math.Atan2(-offset.Y, -offset.X);
        }

        private void MoveBasedOnSpeed()
        {
            if (Target != null && Target.IsDestroyed)
            {
                TargetX = Target.X;
                TargetY = Target.Y;
                TargetZ = Target.Z;
            }

            int playerX = World.Player.X;
            int playerY = World.Player.Y;
            int playerZ = World.Player.Z;

            (int sX, int sY, int sZ) = GetSource();
            int offsetSourceX = sX - playerX;
            int offsetSourceY = sY - playerY;
            int offsetSourceZ = sZ - playerZ;

            (int tX, int tY, int tZ) = GetTarget();
            int offsetTargetX = tX - playerX;
            int offsetTargetY = tY - playerY;
            int offsetTargetZ = tZ - playerZ;

            Vector2 source = new Vector2((offsetSourceX - offsetSourceY) * 22, (offsetSourceX + offsetSourceY) * 22 - offsetSourceZ * 4);

            source.X += Offset.X;
            source.Y += Offset.Y;

            Vector2 target = new Vector2((offsetTargetX - offsetTargetY) * 22, (offsetTargetX + offsetTargetY) * 22 - offsetTargetZ * 4);

            var offset = target - source;
            var distance = offset.Length();
            var frameIndependentSpeed = IntervalInMs * Time.Delta;
            Vector2 s0;

            if (distance > frameIndependentSpeed)
            {
                offset.Normalize();
                s0 = offset * frameIndependentSpeed;
            }
            else
            {
                s0 = target;
            }


            if (distance <= 22)
            {
                RemoveMe();

                return;
            }

            int newOffsetX = (int) (source.X / 22f);
            int newOffsetY = (int) (source.Y / 22f);

            TileOffsetOnMonitorToXY(ref newOffsetX, ref newOffsetY, out int newCoordX, out int newCoordY);

            int newX = playerX + newCoordX;
            int newY = playerY + newCoordY;

            if (newX == tX && newY == tY)
            {
                RemoveMe();

                return;
            }


            IsPositionChanged = true;
            AngleToTarget = (float) Math.Atan2(-offset.Y, -offset.X);

            if (newX != sX || newY != sY)
            {
                // TODO: Z is wrong. We have to calculate an average
                SetSource((ushort) newX, (ushort) newY, (sbyte)sZ);

                Vector2 nextSource = new Vector2((newCoordX - newCoordY) * 22, (newCoordX + newCoordY) * 22 - offsetSourceZ * 4);

                Offset.X = source.X - nextSource.X;
                Offset.Y = source.Y - nextSource.Y;
            }

            Offset.X += s0.X;
            Offset.Y += s0.Y;
        }


        private void RemoveMe()
        {
            CreateExplosionEffect();

            Destroy();
        }

        private static void TileOffsetOnMonitorToXY(ref int ofsX, ref int ofsY, out int x, out int y)
        {
            y = 0;

            if (ofsX == 0)
            {
                x = y = ofsY >> 1;
            }
            else if (ofsY == 0)
            {
                x = ofsX >> 1;
                y = -x;
            }
            else
            {
                int absX = Math.Abs(ofsX);
                int absY = Math.Abs(ofsY);
                x = ofsX;

                if (ofsY > ofsX)
                {
                    if (ofsX < 0 && ofsY < 0)
                    {
                        y = absX - absY;
                    }
                    else if (ofsX > 0 && ofsY > 0)
                    {
                        y = absY - absX;
                    }
                }
                else if (ofsX > ofsY)
                {
                    if (ofsX < 0 && ofsY < 0)
                    {
                        y = -(absY - absX);
                    }
                    else if (ofsX > 0 && ofsY > 0)
                    {
                        y = -(absX - absY);
                    }
                }

                if (y == 0 && ofsY != ofsX)
                {
                    if (ofsY < 0)
                    {
                        y = -(absX + absY);
                    }
                    else
                    {
                        y = absX + absY;
                    }
                }

                y /= 2;
                x += y;
            }
        }
    }
}