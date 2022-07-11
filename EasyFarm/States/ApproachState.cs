// ///////////////////////////////////////////////////////////////////
// This file is a part of EasyFarm for Final Fantasy XI
// Copyright (C) 2013 Mykezero
//  
// EasyFarm is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//  
// EasyFarm is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// If not, see <http://www.gnu.org/licenses/>.
// ///////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using EasyFarm.Classes;
using EasyFarm.Context;
using EasyFarm.UserSettings;
using EasyFarm.ViewModels;
using MemoryAPI;
using Player = EasyFarm.Classes.Player;

namespace EasyFarm.States
{
    /// <summary>
    ///     Moves to target enemies.
    /// </summary>
    public class ApproachState : BaseState
    {
        private bool GettingIntoRange;
        private System.DateTime GettingIntoRangeStart;
        public override bool Check(IGameContext context)
        {
            if (new RestState().Check(context)) return false;

            // Make sure we don't need trusts
            if (new SummonTrustsState().Check(context)) return false;

            // Target dead or null.
            if (!context.Target.IsValid) return false;

            // We should approach mobs that have aggroed or have been pulled. 
            if (context.Target.Status.Equals(Status.Fighting)) return true;

            // Get usable abilities. 
            var usable = context.Config.BattleLists["Pull"].Actions
                .Where(x => ActionFilters.BuffingFilter(context.API, x));

            // Approach when there are no pulling moves available. 
            if (!usable.Any()) return true;

            // Approach mobs if their distance is close. 
            return context.Target.Distance < 8;
        }

        public override void Run(IGameContext context)
        {
            // Target mob if not currently targeted.
            if (context.API.Target.ID != context.Target.Id)
            {
                if (context.API.Player.Status == Status.Fighting)
                {
                    context.API.Windower.SendString(Constants.AttackOff);
                    LogViewModel.Write("Disengaging to target correct mob.");
                    TimeWaiter.Pause(2000);
                    return;
                }
                else
                {
                    Player.SetTarget(context.API, context.Target);
                }
            }

            // Has the user decided we should engage in battle. 
            if (context.Config.IsEngageEnabled)
                if (!context.API.Player.Status.Equals(Status.Fighting) && context.Target.Distance < 25)
                    context.API.Windower.SendString(Constants.AttackTarget);

            // Has the user decided that we should approach targets?
            if (context.Config.IsApproachEnabled)
            {
                // Move to target if out of melee range. 
                var path = context.NavMesh.FindPathBetween(context.API.Player.Position, context.Target.Position);
                if (path.Count > 0)
                {
                    if (path.Count > 1)
                    {
                        context.API.Navigator.DistanceTolerance = 0.5;
                    }
                    else
                    {
                        context.API.Navigator.DistanceTolerance = context.Config.MeleeDistance;
                    }

                    while (path.Count > 0 && path.Peek().Distance(context.API.Player.Position) <= context.API.Navigator.DistanceTolerance)
                    {
                        path.Dequeue();
                    }
                    
                    if (path.Count > 0)
                    {
                        var node = path.Peek();

                        float deltaX = node.X - context.API.Player.Position.X;
                        float deltaY = node.Y - context.API.Player.Position.Y;
                        float deltaZ = node.Z - context.API.Player.Position.Z;
                        context.API.Follow.SetFollowCoords(deltaX, deltaY, deltaZ);
                    }
                    else
                    {
                        context.API.Navigator.FaceHeading(context.Target.Position);
                        context.API.Follow.Reset();
                    }
                }
            } 
            else
            {
                // Face mob. 
                context.API.Navigator.FaceHeading(context.Target.Position);

                // Try to fix the issue where mob is within melee distance but server reports out of range
                if (!GettingIntoRange)
                {
                    try
                    {
                        {
                            var chatEntries = context.API.Chat.ChatEntries.ToList();
                            var outOfRangePattern = new Regex("is out of range.");

                            List<EliteMMO.API.EliteAPI.ChatEntry> matches = chatEntries
                                .Where(x => outOfRangePattern.IsMatch(x.Text)).ToList();

                            bool outOfRange = false;
                            foreach (EliteMMO.API.EliteAPI.ChatEntry m in matches.Where(x => x.Timestamp.ToString() == System.DateTime.Now.ToString()))
                            {
                                outOfRange = true;
                                break;
                            }
                            GettingIntoRange = outOfRange && context.Target.Distance <= 5;
                            if (GettingIntoRange)
                            {
                                // move backward
                                context.API.Windower.SendKeyPress(EliteMMO.API.Keys.DOWN);
                                GettingIntoRangeStart = System.DateTime.Now;
                                LogViewModel.Write("Server out of range, moving.");
                            }
                        }
                    }
                    catch (System.InvalidOperationException e)
                    {
                        //LogViewModel.Write("Chat log updated while trying to recycle, could not check if out of range.  Exception message: " + e.Message);
                    }
                }
                
                if (GettingIntoRange)
                {
                    if ((System.DateTime.Now - GettingIntoRangeStart).TotalMilliseconds >= 250)
                    {
                        GettingIntoRange = false;
                    }
                }
            }
        }
    }
}