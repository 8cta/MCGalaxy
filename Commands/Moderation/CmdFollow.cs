/*
    Copyright 2011 MCForge
    
    Dual-licensed under the Educational Community License, Version 2.0 and
    the GNU General Public License, Version 3 (the "Licenses"); you may
    not use this file except in compliance with the Licenses. You may
    obtain a copy of the Licenses at
    
    http://www.opensource.org/licenses/ecl2.php
    http://www.gnu.org/licenses/gpl-3.0.html
    
    Unless required by applicable law or agreed to in writing,
    software distributed under the Licenses are distributed on an "AS IS"
    BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
    or implied. See the Licenses for the specific language governing
    permissions and limitations under the Licenses.
 */
using System;
namespace MCGalaxy.Commands
{
    public sealed class CmdFollow : Command
    {
        public override string name { get { return "follow"; } }
        public override string shortcut { get { return ""; } }
        public override string type { get { return CommandTypes.Moderation; } }
        public override bool museumUsable { get { return true; } }
        public override LevelPermission defaultRank { get { return LevelPermission.Operator; } }
        public CmdFollow() { }

        public override void Use(Player p, string message)
        {
            if (Player.IsSuper(p)) { MessageInGameOnly(p); return; }
            if (!p.canBuild)
            {
                Player.Message(p, "You're currently being &4possessed%S!");
                return;
            }
            
            bool stealth = false;
            if (message != "")
            {
                if (message == "#")
                {
                    if (p.following != "")
                    {
                        stealth = true;
                        message = "";
                    }
                    else
                    {
                        Help(p);
                        return;
                    }
                }
                else if (message.IndexOf(' ') != -1)
                {
                    if (message.Split(' ')[0] == "#")
                    {
                        if (p.hidden) stealth = true;
                        message = message.Split(' ')[1];
                    }
                }
            }

            Player who = PlayerInfo.Find(message);
            if (message == "" && p.following == "") {
                Help(p);
                return;
            }
            else if (message == "" && p.following != "" || message == p.following)
            {
                who = PlayerInfo.Find(p.following);
                p.following = "";
                if (p.hidden)
                {
                    if (who != null)
                        p.SpawnEntity(who, who.id, who.pos[0], who.pos[1], who.pos[2], who.rot[0], who.rot[1]);
                    if (!stealth)
                    {
                        Command.all.Find("hide").Use(p, "");
                    }
                    else
                    {
                        if (who != null)
                        {
                            Player.Message(p, "You have stopped following " + who.ColoredName + " %Sand remained hidden.");
                        }
                        else
                        {
                            Player.Message(p, "Following stopped.");
                        }
                    }
                    return;
                }
            }
            if (who == null) { Player.Message(p, "Could not find player."); return; }
            else if (who == p) { Player.Message(p, "Cannot follow yourself."); return; }
            else if (who.group.Permission >= p.group.Permission) { MessageTooHighRank(p, "follow", false); return;}
            else if (who.following != "") { Player.Message(p, who.DisplayName + " is already following " + who.following); return; }

            if (!p.hidden) Command.all.Find("hide").Use(p, "");

            if (p.level != who.level) Command.all.Find("tp").Use(p, who.name);
            if (p.following != "")
            {
                who = PlayerInfo.Find(p.following);
                p.SpawnEntity(who, who.id, who.pos[0], who.pos[1], who.pos[2], who.rot[0], who.rot[1]);
            }
            who = PlayerInfo.Find(message);
            p.following = who.name;
            Player.Message(p, "Following " + who.ColoredName + "%S. Use \"/follow\" to stop.");
            p.DespawnEntity(who.id);
        }
        
        public override void Help(Player p)
        {
            Player.Message(p, "/follow <name> - Follows <name> until the command is cancelled");
            Player.Message(p, "/follow # <name> - Will cause /hide not to be toggled");
        }
    }
}
