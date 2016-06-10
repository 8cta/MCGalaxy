/*
 * Written By Jack1312

    Copyright 2011 MCForge
	
	Dual-licensed under the	Educational Community License, Version 2.0 and
	the GNU General Public License, Version 3 (the "Licenses"); you may
	not use this file except in compliance with the Licenses. You may
	obtain a copy of the Licenses at
	
	http://www.opensource.org/licenses/ecl2.php
	http://www.gnu.org/licenses/gpl-3.0.html
	
	Unless required by applicable law or agreed to in writing,
	software distributed under the Licenses are distributed on an "AS IS"
	BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
	or implied. See the Licenses for the specific language governing
	permissions and limitations under the Licenses  
*/
using System.IO;

namespace MCGalaxy.Commands
{
    public sealed class CmdAgree : Command
    {
        public override string name { get { return "agree"; } }
        public override string shortcut { get { return ""; } }
        public override string type { get { return CommandTypes.Other; } }
        public override bool museumUsable { get { return true; } }
        public override LevelPermission defaultRank { get { return LevelPermission.Guest; } }
        public CmdAgree() { }

        public override void Use(Player p, string message)
        {
        	if (Player.IsSuper(p)) { MessageInGameOnly(p); return; }
            if (Server.agreetorulesonentry == false)
            {
                Player.Message(p, "This command can only be used if agree-to-rules-on-entry is enabled!");
                return;
            }
            var agreed = File.ReadAllText("ranks/agreed.txt");
            if (p.hasreadrules == false)
            {
                Player.Message(p, "&9You must read /rules before agreeing!");
                return;
            }
            if ((agreed+" ").Contains(" " + p.name.ToLower() + " ")) //Edited to prevent inner names from working.
            {
                Player.Message(p, "You have already agreed to the rules!");
                return;
            }
            p.agreed = true;
            Player.Message(p, "Thank you for agreeing to follow the rules. You may now build and use commands!");
            string playerspath = "ranks/agreed.txt";
            if (File.Exists(playerspath))
            { 
                //We don't want player "test" to have already agreed if "nate" and "stew" agrred.
                // the preveious one, though, would put "natesteve" which also allows test
                //There is a better way, namely regular expressions, but I'll worry about that later.
                File.AppendAllText(playerspath, " " + p.name.ToLower());  //Ensures every name is seperated by a space.
            }
        
        }

        public override void Help(Player p)
        {
            Player.Message(p, "/agree - Agree to the rules when entering the server");
        }
    }
}
