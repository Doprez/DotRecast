/*
Copyright (c) 2009-2010 Mikko Mononen memon@inside.org
recast4j copyright (c) 2015-2019 Piotr Piastucki piotr@jtilia.org
DotRecast Copyright (c) 2023 Choi Ikpil ikpil@naver.com

This software is provided 'as-is', without any express or implied
warranty.  In no event will the authors be held liable for any damages
arising from the use of this software.
Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it
freely, subject to the following restrictions:
1. The origin of this software must not be misrepresented; you must not
 claim that you wrote the original software. If you use this software
 in a product, an acknowledgment in the product documentation would be
 appreciated but is not required.
2. Altered source versions must be plainly marked as such, and must not be
 misrepresented as being the original software.
3. This notice may not be removed or altered from any source distribution.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using DotRecast.Core.Numerics;

namespace DotRecast.Detour
{
    public static class DtPathUtils
    {
        private const int MAX_STEER_POINTS = 3;

        public static bool GetSteerTarget(DtNavMeshQuery navQuery, RcVec3f startPos, RcVec3f endPos,
            float minTargetDist,
            List<long> path, int pathSize,
            out RcVec3f steerPos, out int steerPosFlag, out long steerPosRef)
        {
            steerPos = RcVec3f.Zero;
            steerPosFlag = 0;
            steerPosRef = 0;

            // Find steer target.
            var straightPath = new List<DtStraightPath>(MAX_STEER_POINTS);
            var result = navQuery.FindStraightPath(startPos, endPos, path, pathSize, ref straightPath, MAX_STEER_POINTS, 0);
            if (result.Failed())
            {
                return false;
            }

            // Find vertex far enough to steer to.
            int ns = 0;
            while (ns < straightPath.Count)
            {
                // Stop at Off-Mesh link or when point is further than slop away.
                if (((straightPath[ns].flags & DtStraightPathFlags.DT_STRAIGHTPATH_OFFMESH_CONNECTION) != 0)
                    || !InRange(straightPath[ns].pos, startPos, minTargetDist, 1000.0f))
                    break;
                ns++;
            }

            // Failed to find good point to steer to.
            if (ns >= straightPath.Count)
                return false;

            steerPos = straightPath[ns].pos;
            steerPos.Y = startPos.Y;
            steerPosFlag = straightPath[ns].flags;
            steerPosRef = straightPath[ns].refs;

            return true;
        }

        public static bool InRange(RcVec3f v1, RcVec3f v2, float r, float h)
        {
            float dx = v2.X - v1.X;
            float dy = v2.Y - v1.Y;
            float dz = v2.Z - v1.Z;
            return (dx * dx + dz * dz) < r * r && MathF.Abs(dy) < h;
        }


        // This function checks if the path has a small U-turn, that is,
        // a polygon further in the path is adjacent to the first polygon
        // in the path. If that happens, a shortcut is taken.
        // This can happen if the target (T) location is at tile boundary,
        // and we're (S) approaching it parallel to the tile edge.
        // The choice at the vertex can be arbitrary,
        // +---+---+
        // |:::|:::|
        // +-S-+-T-+
        // |:::| | <-- the step can end up in here, resulting U-turn path.
        // +---+---+
        public static int FixupShortcuts(ref List<long> path, int npath, DtNavMeshQuery navQuery)
        {
            if (npath < 3)
            {
                return npath;
            }

            // Get connected polygons
            const int maxNeis = 16;
            Span<long> neis = stackalloc long[maxNeis];
            int nneis = 0;

            var status = navQuery.GetAttachedNavMesh().GetTileAndPolyByRef(path[0], out var tile, out var poly);
            if (status.Failed())
            {
                return npath;
            }


            for (int k = tile.polyLinks[poly.index]; k != DtNavMesh.DT_NULL_LINK; k = tile.links[k].next)
            {
                DtLink link = tile.links[k];
                if (link.refs != 0)
                {
                    if (nneis < maxNeis)
                        neis[nneis++] = link.refs;
                }
            }

            // If any of the neighbour polygons is within the next few polygons
            // in the path, short cut to that polygon directly.
            const int maxLookAhead = 6;
            int cut = 0;
            for (int i = Math.Min(maxLookAhead, npath) - 1; i > 1 && cut == 0; i--)
            {
                for (int j = 0; j < nneis; j++)
                {
                    if (path[i] == neis[j])
                    {
                        cut = i;
                        break;
                    }
                }
            }

            if (cut > 1)
            {
                List<long> shortcut = new List<long>();
                shortcut.Add(path[0]);
                shortcut.AddRange(path.GetRange(cut, npath - cut));

                path = shortcut;
                return shortcut.Count;
            }

            return npath;
        }

        public static int MergeCorridorStartMoved(ref List<long> path, int npath, int maxPath, List<long> visited)
        {
            int furthestPath = -1;
            int furthestVisited = -1;

            // Find furthest common polygon.
            for (int i = npath - 1; i >= 0; --i)
            {
                bool found = false;
                for (int j = visited.Count - 1; j >= 0; --j)
                {
                    if (path[i] == visited[j])
                    {
                        furthestPath = i;
                        furthestVisited = j;
                        found = true;
                    }
                }

                if (found)
                {
                    break;
                }
            }

            // If no intersection found just return current path.
            if (furthestPath == -1 || furthestVisited == -1)
            {
                return npath;
            }

            // Concatenate paths.

            // Adjust beginning of the buffer to include the visited.
            List<long> result = new List<long>();
            // Store visited
            for (int i = visited.Count - 1; i > furthestVisited; --i)
            {
                result.Add(visited[i]);
            }

            result.AddRange(path.GetRange(furthestPath, npath - furthestPath));

            path = result;
            return result.Count;
        }

        public static int MergeCorridorEndMoved(ref List<long> path, int npath, int maxPath, List<long> visited)
        {
            int furthestPath = -1;
            int furthestVisited = -1;

            // Find furthest common polygon.
            for (int i = 0; i < npath; ++i)
            {
                bool found = false;
                for (int j = visited.Count - 1; j >= 0; --j)
                {
                    if (path[i] == visited[j])
                    {
                        furthestPath = i;
                        furthestVisited = j;
                        found = true;
                    }
                }

                if (found)
                {
                    break;
                }
            }

            // If no intersection found just return current path.
            if (furthestPath == -1 || furthestVisited == -1)
            {
                return npath;
            }

            // Concatenate paths.
            List<long> result = path.GetRange(0, furthestPath);
            result.AddRange(visited.GetRange(furthestVisited, visited.Count - furthestVisited));

            path = result;
            return result.Count;
        }

        public static int MergeCorridorStartShortcut(ref List<long> path, int npath, int maxPath, List<long> visited)
        {
            int furthestPath = -1;
            int furthestVisited = -1;

            // Find furthest common polygon.
            for (int i = npath - 1; i >= 0; --i)
            {
                bool found = false;
                for (int j = visited.Count - 1; j >= 0; --j)
                {
                    if (path[i] == visited[j])
                    {
                        furthestPath = i;
                        furthestVisited = j;
                        found = true;
                    }
                }

                if (found)
                {
                    break;
                }
            }

            // If no intersection found just return current path.
            if (furthestPath == -1 || furthestVisited <= 0)
            {
                return npath;
            }

            // Concatenate paths.

            // Adjust beginning of the buffer to include the visited.
            List<long> result = visited.GetRange(0, furthestVisited);
            result.AddRange(path.GetRange(furthestPath, npath - furthestPath));

            path = result;
            return result.Count;
        }
    }
}