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

using System;
using fCraft.Events;
using Util = RandomMaze.MazeUtil;

namespace fCraft {

    public class PlantPhysics {

        public static void blockSquash( object sender, PlayerPlacingBlockEventArgs e ) {
            try {
                Player player = e.Player;
                World world = player.World;
                if ( null == world )
                    return;
                lock ( world.SyncRoot ) {
                    if ( null != world.Map && world.IsLoaded && world.plantPhysics ) {
                        if ( e.NewBlock == Block.Plant ) {
                            world.AddPhysicsTask( new PlantTask( world, ( short )e.Coords.X, ( short )e.Coords.Y, ( short )e.Coords.Z ), PlantTask.GetRandomDelay() );
                        }
                        Vector3I z = new Vector3I( e.Coords.X, e.Coords.Y, e.Coords.Z - 1 );
                        if ( world.Map.GetBlock( z ) == Block.Grass && e.NewBlock != Block.Air ) {
                            world.Map.QueueUpdate( new BlockUpdate( null, z, Block.Dirt ) );
                        } else if ( Physics.CanSquash( world.Map.GetBlock( z ) ) && e.NewBlock != Block.Air ) {
                            e.Result = CanPlaceResult.Revert;
                            Player.RaisePlayerPlacedBlockEvent( player, world.Map, z, world.Map.GetBlock( z ), e.NewBlock, BlockChangeContext.Physics );
                            world.Map.QueueUpdate( new BlockUpdate( null, z, e.NewBlock ) );
                        }
                    }
                }
            } catch ( Exception ex ) {
                Logger.Log( LogType.SeriousError, "BlockSquash" + ex );
            }
        }
    }

    public class GrassTask : PhysicsTask //one per world
    {
        private struct Coords //System.Tuple is a class and comparing to this struct causes a significant overhead, thus not used here
        {
            public short X;
            public short Y;
        }

        private const int Delay = 150; //not too often, since it has to scan the whole column at some (x, y)
        private short _i = 0; //current position

        private Coords[] _rndCoords;

        public GrassTask( World world )
            : base( world ) {
            int w, l;
            lock ( world.SyncRoot ) {
                w = _map.Width;
                l = _map.Length;
            }
            _rndCoords = new Coords[w * l]; //up to 250K per world with grass physics
            for ( short i = 0; i < w; ++i )
                for ( short j = 0; j < l; ++j )
                    _rndCoords[i * l + j] = new Coords() { X = i, Y = j };
            Util.RndPermutate( _rndCoords );
        }

        protected override int PerformInternal() {
            if ( _world == null || _map == null )
                return 0;
            if ( !_world.plantPhysics || 0 >= _rndCoords.Length ) //+sanity check, now we are sure that we have at least 1 element in _rndCoords
                return 0;

            if ( _i >= _rndCoords.Length )
                _i = 0;
            Coords c = _rndCoords[_i++];

            bool shadowed = false;
            for ( short z = ( short )( _map.Height - 1 ); z >= 0; --z ) {
                if ( _map == null )
                    return 0;
                Block b = _map.GetBlock( c.X, c.Y, z );

                if ( !shadowed && Block.Dirt == b ) //we have found dirt and there were nothing casting shadows above, so change it to grass and return
                {
                    _map.QueueUpdate( new BlockUpdate( null, c.X, c.Y, z, Block.Grass ) );
                    shadowed = true;
                    continue;
                }

                //since we scan the whole world anyway add the plant task for each not shadowed plant found - it will not harm
                if ( !shadowed && Block.Plant == b ) {
                    _world.AddPlantTask( c.X, c.Y, z );
                    continue;
                }

                if ( shadowed && Block.Grass == b ) //grass should die when shadowed
                {
                    _map.QueueUpdate( new BlockUpdate( null, c.X, c.Y, z, Block.Dirt ) );
                    continue;
                }

                if ( !shadowed )
                    shadowed = CastsShadow( b ); //check if the rest of the column is under a block which casts shadow and thus prevents plants from growing and makes grass to die
            }
            return Delay;
        }

        public static bool CastsShadow( Block block ) {
            switch ( block ) {
                case Block.Air:
                case Block.Glass:
                case Block.Leaves:
                case Block.YellowFlower:
                case Block.RedFlower:
                case Block.BrownMushroom:
                case Block.RedMushroom:
                case Block.Plant:
                case Block.Rope:
                case Block.Fire:
                    return false;
                default:
                    return true;
            }
        }
    }

    public class PlantTask : PhysicsTask {
        private const int MinDelay = 3000;
        private const int MaxDelay = 8000;
        private static Random _r = new Random();

        private enum TreeType {
            NoGrow,
            Normal,
            Palm,
        }

        private short _x, _y, _z;

        public PlantTask( World w, short x, short y, short z )
            : base( w ) {
            _x = x;
            _y = y;
            _z = z;
        }

        static public int GetRandomDelay() {
            return ( _r.Next( MinDelay, MaxDelay ) );
        }

        protected override int PerformInternal() {
            if ( _map.GetBlock( _x, _y, _z ) != Block.Plant ) //superflous task added by grass scanner or deleted plant. just forget it
                return 0;

            TreeType type = TypeByBlock( _map.GetBlock( _x, _y, _z - 1 ) );
            if ( TreeType.NoGrow == type )
                return 0;

            short height = ( short )_r.Next( 4, 7 );
            if ( CanGrow( height ) )
                MakeTrunks( height, type );

            return 0; //non-repeating task
        }

        private bool CanGrow( int height ) //no shadows and enough space
        {
            for ( int z = _z + 1; z < _map.Height; ++z ) {
                if ( GrassTask.CastsShadow( _map.GetBlock( _x, _y, z ) ) )
                    return false;
            }

            for ( int x = _x - 5; x < _x + 5; ++x ) {
                for ( int y = _y - 5; y < _y + 5; ++y ) {
                    for ( int z = _z + 1; z < _z + height; ++z ) {
                        Block b = _map.GetBlock( x, y, z );
                        if ( Block.Air != b && Block.Leaves != b )
                            return false;
                    }
                }
            }

            return true;
        }

        private static TreeType TypeByBlock( Block b ) {
            switch ( b ) {
                case Block.Grass:
                case Block.Dirt:
                    return TreeType.Normal;
                case Block.Sand:
                    return TreeType.Palm;
            }
            return TreeType.NoGrow;
        }

        private void MakeTrunks( short height, TreeType type ) {
            for ( short i = 0; i < height; ++i ) {
                _map.QueueUpdate( new BlockUpdate( null, _x, _y, ( short )( _z + i ), Block.Log ) );
            }
            if ( TreeType.Normal == type )
                TreeGeneration.MakeNormalFoliage( _world, new Vector3I( _x, _y, _z ), height + 1 );
            else
                TreeGeneration.MakePalmFoliage( _world, new Vector3I( _x, _y, _z ), height );
        }
    }
}