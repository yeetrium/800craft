﻿/*        ----
        Copyright (c) 2011-2013 Jon Baker, Glenn Marien and Lao Tszy <Jonty800@gmail.com>
        All rights reserved.

        Redistribution and use in source and binary forms, with or without
        modification, are permitted provided that the following conditions are met:
         * Redistributions of source code must retain the above copyright
              notice, this list of conditions and the following disclaimer.
            * Redistributions in binary form must reproduce the above copyright
             notice, this list of conditions and the following disclaimer in the
             documentation and/or other materials provided with the distribution.
            * Neither the name of 800Craft or the names of its
             contributors may be used to endorse or promote products derived from this
             software without specific prior written permission.

        THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
        ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
        WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
        DISCLAIMED. IN NO EVENT SHALL <COPYRIGHT HOLDER> BE LIABLE FOR ANY
        DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
        (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
        LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
        ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
        (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
        SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
        ----*/
//Copyright (C) <2011 - 2013> Lao Tszy (lao_tszy@yahoo.co.uk)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace fCraft {

    public class LifeHandler {

        private class LifeCommand {
            public string[] Names;
            public string Help;
            public Action<Player, Command> F;
        }

        private class Param {
            public string Name;
            public string Help;
            public Action<Player, Life2DZone, string> SetValue;
        }

        private static readonly Dictionary<string, LifeCommand> Commands = new Dictionary<string, LifeCommand>();
        private static readonly StringBuilder AllCommands = new StringBuilder();
        private static readonly Dictionary<string, Param> Params = new Dictionary<string, Param>();
        private static readonly StringBuilder AllParams = new StringBuilder();

        //static preparation
        static LifeHandler() {
            CreateParams();
            CreateCommands();
            Commands["help"].Help += AllCommands.ToString();
        }

        private static void CreateParams() {
            AddParam( new Param() {
                Name = "Delay",
                Help = "&hDelay in msec before the next life state is drawn. Must be >=20.",
                SetValue = SetDelay
            } );
            AddParam( new Param() {
                Name = "IntrDelay",
                Help = "&hIf >0 the intermediate state of the life is drawn and shown for this anount of timein msec. If 0 the intermediate state is not shown.",
                SetValue = SetHalfDelay
            } );
            AddParam( new Param() {
                Name = "Normal",
                Help = "&hBlock type representing the living cell.",
                SetValue = SetNormal
            } );
            AddParam( new Param() {
                Name = "Empty",
                Help = "&hBlock type representing the empty cell.",
                SetValue = SetEmpty
            } );
            AddParam( new Param() {
                Name = "Dead",
                Help = "&hBlock type representing the dying cell (only relevant for intermediate state).",
                SetValue = SetDead
            } );
            AddParam( new Param() {
                Name = "Newborn",
                Help = "&hBlock type representing the newborn cell (only relevant for intermediate state).",
                SetValue = SetNewborn
            } );
            AddParam( new Param() {
                Name = "Torus",
                Help = "&hBoolean parameter telling if the life area must be understood as a torus (i.e. the top side is connected to the bottom and the left side with the right one.",
                SetValue = SetTorus
            } );
            AddParam( new Param() {
                Name = "AutoReset",
                Help = "&hThis parameter tells if the life must be auto reset after the detection of a short-periodical state. Possible values are None (no), ToInitial (i), ToRandom (r).",
                SetValue = SetAutoReset
            } );
        }

        private static void AddParam( Param p ) {
            if ( Params.Count > 0 )
                AllParams.Append( ", " );
            AllParams.Append( p.Name );

            Params.Add( p.Name.ToLower(), p );
        }

        private static void CreateCommands() {
            //assuming each command has at least one name and no names repeating. Will throw on start if it is not the case.
            AddCommand( new LifeCommand() {
                Names = new string[] { "help", "h" },
                Help = "&hPrints help on commands. Usage: /life help <command|param>. For the list of parameters type '/life help set'. Commands are: ",
                F = OnHelp
            } );
            AddCommand( new LifeCommand() {
                Names = new string[] { "create", "new" },
                Help = "&hCreates a new life. Usage: /life create <name>. Then mark two blocks to define a *flat* rectangle. The life is created stopped and with default settings. After that you can set params by 'set' command and start it by 'start' command.",
                F = OnCreate
            } );
            AddCommand( new LifeCommand() {
                Names = new string[] { "delete", "del", "remove" },
                Help = "&hDeletes a life. Usage: /life delete <name>. If this life exists it will be stopped and removed from the map book keeping.",
                F = OnDelete
            } );
            AddCommand( new LifeCommand() {
                Names = new string[] { "start", "run" },
                Help = "&hStarts a stopped life. Usage: /life start <name>. If this life exists and is stopped it will be started. Otherwise nothing happens.",
                F = OnStart
            } );
            AddCommand( new LifeCommand() {
                Names = new string[] { "stop", "pause" },
                Help = "&hStops a life. Usage: /life stop <name>. If this life exists and is started it will be stopped. Otherwise nothing happens.",
                F = OnStop
            } );
            AddCommand( new LifeCommand() {
                Names = new string[] { "set" },
                Help = "&hSets a life parameter. Usage: /life set <name> <param>=<value>[| <param>=<value>]. Sets parameter 'param' value for the life 'name'. Parameters are: "
                    + AllParams.ToString(),
                F = OnSet
            } );
            AddCommand( new LifeCommand() {
                Names = new string[] { "list", "ls" },
                Help = "&hPrints all lifes in players world. Usage: /life list [started|stopped]",
                F = OnList
            } );
            AddCommand( new LifeCommand() {
                Names = new string[] { "print", "cat" },
                Help = "&hPrints this life settings. Usage: /life print <name>",
                F = OnPrint
            } );
        }

        private static void AddCommand( LifeCommand c ) {
            if ( Commands.Count > 0 )
                AllCommands.Append( ", " );
            AllCommands.Append( c.Names[0] );
            foreach ( string name in c.Names )
                Commands.Add( name.ToLower(), c );
        }

        //processing
        private string _name;

        private World _world;
        private Life2DZone _life;

        public static void ProcessCommand( Player p, Command cmd ) {
            string command = cmd.Next();
            if ( String.IsNullOrWhiteSpace( command ) ) {
                p.Message( "&WLife command is missing or empty" );
                return;
            }
            LifeCommand c;
            if ( !Commands.TryGetValue( command.ToLower(), out c ) ) {
                p.Message( "&WUnknown life command " + command + ". &hType '/life help' for the list of commands." );
                return;
            }
            c.F( p, cmd );
        }

        private static string AliasesStr( LifeCommand cmd ) {
            if ( cmd.Names.Length < 2 )
                return "";
            StringBuilder sb = new StringBuilder( "&hAliases: " );
            for ( int i = 1; i < cmd.Names.Length; ++i ) {
                if ( i > 1 )
                    sb.Append( ", " );
                sb.Append( cmd.Names[i] );
            }
            return sb.Append( "." ).ToString();
        }

        private static void OnHelp( Player p, Command cmd ) {
            string cOrP = cmd.Next();
            if ( String.IsNullOrWhiteSpace( cOrP ) ) {
                p.Message( "&hLife commands are: " + AllCommands.ToString() +
                    ".\nType '/life help <command|param> for detailed command or param info. Type '/life help set' for the list of parameters." );
                return;
            }
            LifeCommand c;
            string help;
            if ( !Commands.TryGetValue( cOrP.ToLower(), out c ) ) {
                Param param;
                if ( !Params.TryGetValue( cOrP.ToLower(), out param ) ) {
                    p.Message( "&WUnknown life command/parameter " + cOrP + ". &hType '/life help' for the list of commands." );
                    return;
                }
                help = param.Help;
            } else
                help = AliasesStr( c ) + c.Help;

            p.Message( help );
        }

        private bool CheckAndGetLifeZone( Player p, Command cmd ) {
            _life = null;
            _world = null;
            _name = cmd.Next();
            if ( String.IsNullOrWhiteSpace( _name ) ) {
                p.Message( "&WLife name is missing or empty" );
                return false;
            }

            _world = p.World;
            if ( null == _world ) {
                p.Message( "&WYou are in limbo state. Prepare for eternal torment." );
                return false;
            }

            lock ( _world.SyncRoot ) {
                if ( null == _world.Map )
                    return false;
                _life = _world.GetLife( _name );
                return true;
            }
        }

        private static LifeHandler GetCheckedLifeHandler( Player p, Command cmd ) {
            LifeHandler handler = new LifeHandler();
            if ( !handler.CheckAndGetLifeZone( p, cmd ) )
                return null;
            if ( null == handler._life ) {
                p.Message( "&WLife " + handler._name + " does not exist." );
                return null;
            }
            return handler;
        }

        private static void OnCreate( Player p, Command cmd ) {
            LifeHandler handler = new LifeHandler();
            if ( !handler.CheckAndGetLifeZone( p, cmd ) )
                return;
            if ( !handler.CheckWorldPermissions( p ) )
                return;
            if ( null != handler._life ) {
                p.Message( "&WLife with such name exists already, choose another" );
                return;
            }

            p.SelectionStart( 2, handler.LifeCreateCallback, null, Permission.DrawAdvanced );
            p.MessageNow( "Select life zone: place/remove a block or type /Mark to use your location." );
        }

        private void LifeCreateCallback( Player player, Vector3I[] marks, object state ) {
            try {
                lock ( _world.SyncRoot ) {
                    if ( !CheckWorldPermissions( player ) )
                        return;
                    if ( null == _world.Map )
                        return;
                    if ( null != _world.GetLife( _name ) ) //check it again, since smone could create it in between
					{
                        player.Message( "&WLife with such name exists already, choose another" );
                        return;
                    }
                    Life2DZone life = new Life2DZone( _name, _world.Map, marks, player, ( player.Info.Rank.NextRankUp ?? player.Info.Rank ).Name );
                    if ( _world.TryAddLife( life ) )
                        player.Message( "&yLife was created. Named " + _name );
                    else
                        player.Message( "&WCoulnd't create life for some reason unknown." ); //really unknown: we are under a lock so nobody could create a life with the same name in between
                }
            } catch ( Exception e ) {
                player.Message( "&WCreate life error: " + e.Message );
            }
        }

        private static void OnStart( Player p, Command cmd ) {
            LifeHandler handler = GetCheckedLifeHandler( p, cmd );
            if ( null == handler )
                return;
            if ( !handler.CheckChangePermissions( p ) )
                return;
            handler._life.Start();
            p.Message( "&yLife " + handler._life.Name + " is started" );
        }

        private static void OnStop( Player p, Command cmd ) {
            LifeHandler handler = GetCheckedLifeHandler( p, cmd );
            if ( null == handler )
                return;
            if ( !handler.CheckChangePermissions( p ) )
                return;
            handler._life.Stop();
            p.Message( "&yLife " + handler._life.Name + " is stopped" );
        }

        private static void OnDelete( Player p, Command cmd ) {
            LifeHandler handler = GetCheckedLifeHandler( p, cmd );
            if ( null == handler )
                return;
            if ( !handler.CheckChangePermissions( p ) )
                return;
            handler._life.Stop();
            handler._world.DeleteLife( handler._name );
            p.Message( "&yLife " + handler._life.Name + " is deleted" );
        }

        private static void OnList( Player p, Command cmd ) {
            World w = p.World;
            if ( null == w ) {
                p.Message( "&WYou are in limbo state. Prepare for eternal torment." );
                return;
            }
            string param = cmd.Next();
            Func<Life2DZone, bool> f = l => true;
            if ( !string.IsNullOrWhiteSpace( param ) ) {
                switch (param)
                {
                    case "started":
                        f = l => !l.Stopped;
                        break;
                    case "stopped":
                        f = l => l.Stopped;
                        break;
                    default:
                        p.Message( "&WUnrecognised parameter " + param + ". Ignored.\n" );
                        break;
                }
            }
            int i = 0;
            foreach ( Life2DZone life in w.GetLifes().Where( life => f( life ) ) ) {
                if ( i++ > 0 )
                    p.Message( ", " );
                p.Message( ( life.Stopped ? "&8" : "&2" ) + life.Name );
            }
        }

        private static void OnPrint( Player p, Command cmd ) {
            LifeHandler handler = GetCheckedLifeHandler( p, cmd );
            if ( null == handler )
                return;
            Life2DZone l = handler._life;
            p.Message( "&y" + l.Name + ": " + ( l.Stopped ? "stopped" : "started" ) + ", delay " + l.Delay +
                ", intermediate delay " + l.HalfStepDelay + ", is" + ( l.Torus ? "" : " not" ) + " on torus, " +
                "auto reset strategy is " + Enum.GetName( typeof( AutoResetMethod ), l.AutoReset ) +
                ", owner is " + l.CreatorName +
                ", changable by " + l.MinRankToChange +
                ", block types: " + l.Normal + " is normal, " + l.Empty + " is empty, " + l.Dead + " is dead, " + l.Newborn + " is newborn" );
        }

        private static void OnSet( Player p, Command cmd ) {
            LifeHandler handler = GetCheckedLifeHandler( p, cmd );
            if ( null == handler )
                return;
            if ( !handler.CheckChangePermissions( p ) )
                return;

            string paramStr = cmd.Next();
            if ( string.IsNullOrWhiteSpace( paramStr ) ) {
                p.Message( "&WEmpty parameter name. &hAccepted names are " + AllParams.ToString() );
                return;
            }
            Param param;
            if ( !Params.TryGetValue( paramStr, out param ) ) {
                p.Message( "&WUknown parameter name" + paramStr + ". &hAccepted names are " + AllParams.ToString() );
                return;
            }
            string val = cmd.Next();
            if ( string.IsNullOrWhiteSpace( val ) ) {
                p.Message( "&WEmpty value." );
                return;
            }
            param.SetValue( p, handler._life, val );
        }

        private static void SetDelay( Player p, Life2DZone life, string val ) {
            int delay;
            if ( !int.TryParse( val, out delay ) || delay <= 20 ) {
                p.Message( "&WExpected integer value >=20 as delay" );
                return;
            }
            life.Delay = delay;
            p.Message( "&yStep delay set to " + val );
        }

        private static void SetHalfDelay( Player p, Life2DZone life, string val ) {
            int delay;
            if ( !int.TryParse( val, out delay ) || delay < 0 ) {
                p.Message( "&WExpected non-negative integer value as intermediate delay" );
                return;
            }
            life.HalfStepDelay = delay;
            p.Message( "&yIntermediate step delay set to " + val );
        }

        private static void SetNormal( Player p, Life2DZone life, string val ) {
            Block b = Map.GetBlockByName( val );
            if ( b == Block.Undefined ) {
                p.Message( "&WUnrecognized block name " + val );
                return;
            }
            life.Normal = b;
            p.Message( "&yNormal block set to " + val );
        }

        private static void SetEmpty( Player p, Life2DZone life, string val ) {
            Block b = Map.GetBlockByName( val );
            if ( b == Block.Undefined ) {
                p.Message( "&WUnrecognized block name " + val );
                return;
            }
            life.Empty = b;
            p.Message( "&yEmpty block set to " + val );
        }

        private static void SetDead( Player p, Life2DZone life, string val ) {
            Block b = Map.GetBlockByName( val );
            if ( b == Block.Undefined ) {
                p.Message( "&WUnrecognized block name " + val );
                return;
            }
            life.Dead = b;
            p.Message( "&yDead block set to " + val );
        }

        private static void SetNewborn( Player p, Life2DZone life, string val ) {
            Block b = Map.GetBlockByName( val );
            if ( b == Block.Undefined ) {
                p.Message( "&WUnrecognized block name " + val );
                return;
            }
            life.Newborn = b;
            p.Message( "&yNewborn block set to " + val );
        }

        private static void SetTorus( Player p, Life2DZone life, string val ) {
            bool torus;
            if ( !bool.TryParse( val, out torus ) ) {
                p.Message( "&WExpected 'true' or 'false' as torus parameter value" );
                return;
            }
            life.Torus = torus;
            p.Message( "&yTorus param set to " + val );
        }

        private static void SetAutoReset( Player p, Life2DZone life, string val ) {
            AutoResetMethod method;
            val = val.ToLower();
            switch (val)
            {
                case "none":
                case "no":
                    method = AutoResetMethod.None;
                    break;
                case "i":
                case "toinitial":
                    method = AutoResetMethod.ToInitial;
                    break;
                case "rnd":
                case "r":
                case "torandom":
                    method = AutoResetMethod.ToRandom;
                    break;
                default:
                    p.Message( "&WUnrecognized auto reset method " + val + ".\n&h Type '/life help AutoReset' to see all the possible values." );
                    return;
            }
            life.AutoReset = method;
            p.Message( "&yAutoReset param set to " + Enum.GetName( typeof( AutoResetMethod ), method ) );
        }

        private bool CheckWorldPermissions( Player p ) {
            if ( !p.Info.Rank.AllowSecurityCircumvention ) {
                SecurityCheckResult buildCheck = _world.BuildSecurity.CheckDetailed( p.Info );
                switch ( buildCheck ) {
                    case SecurityCheckResult.BlackListed:
                        p.Message( "Cannot add life to world {0}&S: You are barred from building here.",
                                        p.ClassyName );
                        return false;
                    case SecurityCheckResult.RankTooLow:
                        p.Message( "Cannot add life to world {0}&S: You are not allowed to build here.",
                                        p.ClassyName );
                        return false;
                }
            }
            return true;
        }

        private bool CheckChangePermissions( Player p ) {
            if ( string.IsNullOrWhiteSpace( _life.CreatorName ) || p.Name == _life.CreatorName )
                return true;
            if ( string.IsNullOrWhiteSpace( _life.MinRankToChange ) )
                return true;
            Rank r;
            if ( !RankManager.RanksByName.TryGetValue( _life.MinRankToChange, out r ) ) {
                string prevRank = _life.MinRankToChange;
                r = RankManager.LowestRank.NextRankUp ?? RankManager.LowestRank;
                _life.MinRankToChange = r.Name;
                p.Message( "&WRank " + prevRank + " couldn't be found. Updated to " + r.Name );
            }
            if ( p.Info.Rank >= r )
                return true;
            p.Message( "&WYour rank is too low to change this life." );
            return false;
        }
    }
}