/*
 * The following code is an implementation of the A* pathfinding algorithm in C# with XNA, which 
 * determines the shortest valid path between two tiles (while avoiding impassable areas).  In the
 * engine I codeveloped for a 2D platforming video game, this pathfinding is most notably used for
 * enemy AI.  For example, one type of implemented enemy moves toward the closest player character and
 * deals damage on contact.
 */

namespace CharacterPlatformer.Utilities
{
    class AI
    {
        AStar aStar;

        public AI()
        {
            aStar = new AStar();
        }

        //Finds the optimal path from startCoords to destinationCoords, without factoring in impassable terrain.
        //This entails initial repeated diagonal movement (in this implementation) followed by vertical or horizontal movement.
        public float ManhattanDiagonalCost(Vector2 startCoords, Vector2 destinationCoords)
        {
            return aStar.ManhattanDiagonalCost(startCoords, destinationCoords);
        }

        //Looks at the positions of all the player-controlled characters on the field and returns the character closest to the inputted startCoords.
        //Returns null if no characters exist.
        public Character FindClosestNonPaintbrushPlayerUsingManhattanDiagonalCost(Vector2 tileCoords)
        {
            float smallestManhattanCost = -1;
            Character closestPlayer = null;
            foreach (Character player in Level.Players)  //Note: Level.Players is a static variable defined elsewhere.
            {
                if (player.isAlive && !(player is Paintbrush))  //Note: Paintbrush is a special playable character that cannot be targeted by enemies.
                {
                    if (smallestManhattanCost == -1)  //If this is the first player we have looked at.
                    {
                        smallestManhattanCost = ManhattanDiagonalCost(tileCoords, Collision.FindWhatTileOnScreenCoordsAreIn(player.characterSprite.GetCenter()));
                        closestPlayer = player;
                    }
                    else
                    {
                        float newManhattanCost = ManhattanDiagonalCost(tileCoords, Collision.FindWhatTileOnScreenCoordsAreIn(player.characterSprite.GetCenter()));
                        if (newManhattanCost < smallestManhattanCost)
                        {
                            closestPlayer = player;
                            smallestManhattanCost = newManhattanCost;
                        }
                    }
                }
            }

            return closestPlayer;
        }

        //Finds the shortest path to the destination using the AStar algorithm.  Returns an ordered list representing the 
        //sequence of tiles you should visit to reach the destination.  Returns null if the tile is unreachable.
        public List<Vector2> FindPathAStar(Vector2 startCoords, Vector2 goalCoords)
        {
            //Ensure that the target player resides on a potentially reachable tile (i.e. that the tile is not off the edge of the screen and is passable itself).
            int destinationX = (int)goalCoords.X;
            int destinationY = (int)goalCoords.Y;
            if (destinationX < 0 || destinationX >= Level.tiles.GetLength(0) || destinationY < 0 || destinationY >= Level.tiles.GetLength(1)
                || Level.tiles[destinationX, destinationY].collision == TileCollision.Impassable)
                return null;

            return aStar.FindPath(startCoords, goalCoords);
        }




        //The implementation of the A* algorithm.
        private class AStar
        {
            enum ListType
            {
                Open = 0, //Open nodes are adjacent to a closed node and will be investigated once the algorithm thinks they may well be a part of the shortest path.
                Closed = 1, //Open nodes are moved to the closed list once they have been investigated.
                None = 2  //Nodes that have not been encountered yet are defaulted to ListType.None.
            }

            enum MovementDirection
            {
                Up = 0,
                UpRight = 1,
                Right = 2,
                DownRight = 3,
                Down = 4,
                DownLeft = 5,
                Left = 6,
                UpLeft = 7
            }

            class ListCoordinateInformation
            {
                public ListType listType = ListType.None;  //This variable is changed to ListType.Open or ListType.Closed as it becomes used by the A* algorithm.
                public Vector2 selfCoordinate;

                //This is a reference to the node in the open list that is associated with this coordinate.  If the coordinate is not in
                //the open list (if it has not been visited, etc.), selfOpenListNode is set to null.
                public HeapNode selfOpenListNode = null;

                //Used to trace back along the path from the destination to the start.
                //The parentCoordinate of the starting tile is null.
                public ListCoordinateInformation parentCoordinate;
            }

            MinHeap openList;  //The MinHeap class is listed later on in this file.  The algorithm does not require the use of a MinHeap for the closed list.
            ListCoordinateInformation[,] listCoordinateInformation;

            //The "cost" (i.e. the number to increase g by) of moving to a diagonal square and one that is left/right or up/down.
            //This is calculated knowing our tiles are 32x24 pixels.
            const float DiagonalCost = 40;
            const float LeftRightCost = 32;
            const float UpDownCost = 24;

            public AStar()
            {
                openList = new MinHeap();
                listCoordinateInformation = new ListCoordinateInformation[Level.tiles.GetLength(0), Level.tiles.GetLength(1)];

                //Initialize the self coordinate of every element in the closedList array (i.e. every tile in the level).
                for (int i = 0; i < listCoordinateInformation.GetLength(0); i++)
                    for (int j = 0; j < listCoordinateInformation.GetLength(1); j++)
                    {
                        listCoordinateInformation[i, j] = new ListCoordinateInformation();
                        listCoordinateInformation[i, j].selfCoordinate = new Vector2(i, j);
                    }
            }

            //This method will determine the shortest path one can take to get from your starting position to your destination,
            //taking into account obstacles such as impassible tiles.  startCoords and goalCoords will range from [0, 0] to (40, 30).
            //The coordinates of the tile you should move towards are returned (the tile you are currently on is not included).
            //If no path exists, null is returned.
            public List<Vector2> FindPath(Vector2 startCoords, Vector2 goalCoords)
            {
                //Ensure that the open and closed lists are empty.
                openList = new MinHeap();
                listCoordinateInformation = new ListCoordinateInformation[Level.tiles.GetLength(0), Level.tiles.GetLength(1)];

                //Initialize the self coordinate of every element in the closedList array (i.e. every tile in the level).
                for (int i = 0; i < listCoordinateInformation.GetLength(0); i++)
                    for (int j = 0; j < listCoordinateInformation.GetLength(1); j++)
                    {
                        listCoordinateInformation[i, j] = new ListCoordinateInformation();
                        listCoordinateInformation[i, j].selfCoordinate = new Vector2(i, j);
                    }

                //Add the starting tile to the open list.
                HeapNode startingNode = new HeapNode(0, ManhattanDiagonalCost(startCoords, goalCoords), startCoords);
                openList.Insert(startingNode);
                listCoordinateInformation[Convert.ToInt32(startCoords.X), Convert.ToInt32(startCoords.Y)].selfOpenListNode = startingNode;

                if (FindPathRecursive(goalCoords))  //If a path exists.
                {
                    List<Vector2> reversePath = new List<Vector2>();  //Stores the path in reverse, since we need to trace the path backwards from the goal coords to the starting coords.
                    ListCoordinateInformation currentCoordinateInfo = listCoordinateInformation[Convert.ToInt32(goalCoords.X), Convert.ToInt32(goalCoords.Y)];
                    while (currentCoordinateInfo.parentCoordinate != null)  //While we have not reached our starting coords...
                    {
                        reversePath.Add(currentCoordinateInfo.selfCoordinate);
                        currentCoordinateInfo = currentCoordinateInfo.parentCoordinate;
                    }

                    List<Vector2> forwardPath = new List<Vector2>();
                    for (int i = reversePath.Count - 1; i >= 0; i--)  //Finding the forward sequence of tiles to visit by iterating backwards through the list that stores the reverse.
                        forwardPath.Add(reversePath[i]);

                    return forwardPath;
                }
                else  //No path exists.
                    return null;
            }

            //A helper method for FindPath() that recursively finds the shortest path to the goal coords.
            //Returns true once it has found the path and false if no path exists.
            private bool FindPathRecursive(Vector2 goalCoords)
            {
                //No path to the goal coords exists.
                if (openList.isEmpty())
                    return false;

                //Find the current square and move it from the open to the closed list.
                HeapNode currentSquare = openList.RemoveRoot();
                int currentSquareXCoord = (int)currentSquare.coordinates.X;
                int currentSquareYCoord = (int)currentSquare.coordinates.Y;
                listCoordinateInformation[currentSquareXCoord, currentSquareYCoord].listType = ListType.Closed;

                //If you have reached the goal and therefore found a path to it:
                if (listCoordinateInformation[currentSquareXCoord, currentSquareYCoord].selfCoordinate == goalCoords)
                    return true;

                //Check the 8 squares around the current square to see if they are walkable, not around a corner and therefore blocked,
                //not off the edge of the screen, and not on the closed list.  If this check passes, add them to the open list for future
                //investigation if they are not on there already.  If they are, see if you have found a shorter path than before to get to them.
                foreach (MovementDirection directionToMove in Enum.GetValues(typeof(MovementDirection)))
                {
                    //First, we determine our destination coordinates based upon our current coordinations and desired movement direction.
                    //This destination square will be one of the (up to) 8 adjacent squares surrounding our current position.
                    Vector2 destinationCoords = FindDestinationCoords(currentSquare.coordinates, directionToMove);
                    int destinationXCoord = Convert.ToInt32(destinationCoords.X);
                    int destinationYCoord = Convert.ToInt32(destinationCoords.Y);

                    //If the attempted move is physically possible (given physical obstacles) and does not already reside in the closed list.
                    if (MoveIsValid(destinationXCoord, destinationYCoord, directionToMove))
                    {
                        //If the coordinate is not on the open or closed list, add it to the open list.
                        if (listCoordinateInformation[destinationXCoord, destinationYCoord].listType == ListType.None)
                        {
                            HeapNode destinationHeapNode = new HeapNode(currentSquare.gValue + FindMovementCost(directionToMove), ManhattanDiagonalCost(destinationCoords, goalCoords), destinationCoords);
                            openList.Insert(destinationHeapNode);
                            listCoordinateInformation[destinationXCoord, destinationYCoord].listType = ListType.Open;
                            listCoordinateInformation[destinationXCoord, destinationYCoord].selfOpenListNode = destinationHeapNode;
                            listCoordinateInformation[destinationXCoord, destinationYCoord].parentCoordinate = listCoordinateInformation[currentSquareXCoord, currentSquareYCoord];
                        }
                        //If the coordinate is on the open list, update the path to it if we have found a better one.
                        else
                        {
                            ListCoordinateInformation currentInfo = listCoordinateInformation[currentSquareXCoord, currentSquareYCoord];
                            ListCoordinateInformation destinationInfo = listCoordinateInformation[destinationXCoord, destinationYCoord];
                            if (destinationInfo.selfOpenListNode.gValue > currentInfo.selfOpenListNode.gValue + FindMovementCost(directionToMove))
                            {
                                //We have found a shorter path to the node.  Update the g and f values of the node in the open list to the new, smaller values.
                                destinationInfo.parentCoordinate = currentInfo;
                                destinationInfo.selfOpenListNode.gValue = currentInfo.selfOpenListNode.gValue + FindMovementCost(directionToMove);
                                destinationInfo.selfOpenListNode.fValue = destinationInfo.selfOpenListNode.gValue + destinationInfo.selfOpenListNode.hValue;

                                //Rebalance the heap since we have updated the fValue of a node.  The node is guaranteed to have a smaller fValue (not larger),
                                //so we can use the RebalanceHeapTravelingUp() method.
                                openList.RebalanceHeapTravelingUp(destinationInfo.selfOpenListNode);
                            }
                        }
                    }
                }

                return FindPathRecursive(goalCoords);  //Keep running this method until we reach the goal or determine that no path exists.
            }

            //Returns true if the square is on-screen, not impassible, and not on the closed list.  This is used when the 8 squares 
            //around the current squares are checked to see if they should be added to the open list and investigated more thoroughly.
            private bool MoveIsValid(int destinationXCoord, int destinationYCoord, MovementDirection directionToMove)
            {
                bool validMove = false;
                //First, we determine whether or not the destination tile resides on the screen, is not part of the closed list, and is walkable.
                if (destinationXCoord >= 0 && destinationXCoord < Level.tiles.GetLength(0) &&
                    destinationYCoord >= 0 && destinationYCoord < Level.tiles.GetLength(1) &&
                    listCoordinateInformation[destinationXCoord, destinationYCoord].listType != ListType.Closed &&
                    Level.tiles[destinationXCoord, destinationYCoord].collision != TileCollision.Impassable)
                {
                    //Checks whether an attempted diagonal move is blocked by one or more tiles immediately to the side or above/below you
                    //(in other words, whether you would have to move through the corner of an impassable tile to move diagonally in that direction).
                    //"Platform" tiles can be moved up through but not down through and therefore will only block downward movement.
                    if (directionToMove == MovementDirection.UpRight || directionToMove == MovementDirection.DownRight
                        || directionToMove == MovementDirection.DownLeft || directionToMove == MovementDirection.UpLeft
                        || directionToMove == MovementDirection.Down)
                    {
                        switch (directionToMove)
                        {
                            case MovementDirection.UpRight:
                                validMove = (Level.tiles[destinationXCoord - 1, destinationYCoord].collision != TileCollision.Impassable &&
                                    Level.tiles[destinationXCoord, destinationYCoord + 1].collision != TileCollision.Impassable);
                                break;
                            case MovementDirection.DownRight:
                                validMove = (Level.tiles[destinationXCoord - 1, destinationYCoord].collision != TileCollision.Impassable &&
                                    Level.tiles[destinationXCoord, destinationYCoord - 1].collision != TileCollision.Impassable &&
                                    Level.tiles[destinationXCoord, destinationYCoord].collision != TileCollision.Platform);
                                break;
                            case MovementDirection.DownLeft:
                                validMove = (Level.tiles[destinationXCoord + 1, destinationYCoord].collision != TileCollision.Impassable &&
                                    Level.tiles[destinationXCoord, destinationYCoord - 1].collision != TileCollision.Impassable &&
                                    Level.tiles[destinationXCoord, destinationYCoord].collision != TileCollision.Platform);
                                break;
                            case MovementDirection.UpLeft:
                                validMove = (Level.tiles[destinationXCoord + 1, destinationYCoord].collision != TileCollision.Impassable &&
                                    Level.tiles[destinationXCoord, destinationYCoord + 1].collision != TileCollision.Impassable);
                                break;
                            case MovementDirection.Down:
                                validMove = Level.tiles[destinationXCoord, destinationYCoord].collision != TileCollision.Platform;
                                break;
                        }
                    }
                    else
                        validMove = true;
                }

                return validMove;
            }

            //Calculate the coordinates of the destination tile based upon the desired movement direction.
            //This method does not check to see if the destination is off the edge of the screen.
            private Vector2 FindDestinationCoords(Vector2 currentCoords, MovementDirection directionToMove)
            {
                Vector2 destinationCoords = new Vector2(currentCoords.X, currentCoords.Y);
                switch (directionToMove)
                {
                    case MovementDirection.Up:
                        destinationCoords.Y -= 1;
                        break;
                    case MovementDirection.UpRight:
                        destinationCoords.X += 1;
                        destinationCoords.Y -= 1;
                        break;
                    case MovementDirection.Right:
                        destinationCoords.X += 1;
                        break;
                    case MovementDirection.DownRight:
                        destinationCoords.X += 1;
                        destinationCoords.Y += 1;
                        break;
                    case MovementDirection.Down:
                        destinationCoords.Y += 1;
                        break;
                    case MovementDirection.DownLeft:
                        destinationCoords.X -= 1;
                        destinationCoords.Y += 1;
                        break;
                    case MovementDirection.Left:
                        destinationCoords.X -= 1;
                        break;
                    case MovementDirection.UpLeft:
                        destinationCoords.X -= 1;
                        destinationCoords.Y -= 1;
                        break;
                }

                return destinationCoords;
            }

            //Calculate the cost of moving in the inputted direction.
            private float FindMovementCost(MovementDirection directionToMove)
            {
                if (directionToMove == MovementDirection.Down || directionToMove == MovementDirection.Up)
                    return UpDownCost;
                else if (directionToMove == MovementDirection.Left || directionToMove == MovementDirection.Right)
                    return LeftRightCost;
                else
                    return DiagonalCost;
            }

            //Finds the Manhattan distance between two coordinates (i.e. the distance on the condition that 
            //only horizontal and vertical movements are made), and multiplies it by the cost required to move
            //each direction.  Impassable terrain is not factored in.
            public float ManhattanCost(Vector2 startCoords, Vector2 destinationCoords)
            {
                return (LeftRightCost * Math.Abs(Math.Abs(startCoords.X) - Math.Abs(destinationCoords.X)) +
                    UpDownCost * Math.Abs(Math.Abs(startCoords.Y) - Math.Abs(destinationCoords.Y)));
            }

            //Finds the optimal path from startCoords to destinationCoords, without factoring in impassable terrain.
            //This entails initial repeated diagonal movement (in this implementation) followed by vertical or horizontal movement.
            public float ManhattanDiagonalCost(Vector2 startCoords, Vector2 destinationCoords)
            {
                bool destinationRight = ((destinationCoords.X - startCoords.X) > 0);
                bool destinationBelow = ((destinationCoords.Y - startCoords.Y) > 0);

                float diagonalComponentCost = 0.0f;
                Vector2 currentCoords = startCoords;

                //As long as we are not yet vertically or horizontally aligned with our destination, we will continue to 
                //move in a diagonal path for increased efficiency.
                while (destinationCoords.X != currentCoords.X && destinationCoords.Y != currentCoords.Y)
                {
                    if (destinationRight)
                    {
                        if (destinationBelow)
                            currentCoords = currentCoords + new Vector2(1.0f, 1.0f);
                        else
                            currentCoords = currentCoords + new Vector2(1.0f, -1.0f);
                    }
                    else
                    {
                        if (destinationBelow)
                            currentCoords = currentCoords + new Vector2(-1.0f, 1.0f);
                        else
                            currentCoords = currentCoords + new Vector2(-1.0f, -1.0f);
                    }
                    diagonalComponentCost += DiagonalCost;
                }

                //In order to determine our final cost after diagonal movement has placed us in-line (either
                //horizontally, vertically, or both) with our destination, we simply use the Manhattan method
                //(which does not use diagonal movement).
                return diagonalComponentCost + ManhattanCost(currentCoords, destinationCoords);
            }
        }
    }




    //The implementation of a node in the MinHeap.
    public class HeapNode
    {

        public float fValue;  //fValue = gValue + hValue.  We will be using fValue when sorting the heap.
        public float gValue;  //gValue is the movement cost to move from startCoords to the currentCoords.
        public float hValue;  //hValue is the estimated movement cost to move from currentCoords to the destinationCoords.
        public HeapNode leftChild, rightChild, parent;
        public Vector2 coordinates; //Ranging from (0 to Level.tiles.getLength(0), 0 to Level.tiles.getLength(1)).  Defaults to (-1, -1) if coordinates are not entered.

        public HeapNode()
        {
            fValue = -1; gValue = -1; hValue = -1;
            leftChild = null; rightChild = null; parent = null;
            coordinates = new Vector2(-1, -1);
        }
        public HeapNode(float g, float h, Vector2 nodeCoordinates)
        {
            gValue = g;
            hValue = h;
            fValue = g + h;
            leftChild = null; rightChild = null; parent = null;
            coordinates = nodeCoordinates;
        }
    }




    //The implementation of a MinHeap.
    public class MinHeap
    {
        int numNodes = 0;
        public HeapNode rootNode;
        int depth = -1;  //The maximum depth of the current heap.  A depth of -1 applies only to an empty heap.
        int firstOpenDepth = 1;

        //Inserts a node into the MinHeap.
        public void Insert(HeapNode node)
        {

            if (numNodes == 0)  //If our heap is empty, the node to insert is the heap's new root.
                rootNode = node;
            else  //Otherwise, insert the node in the leftmost position at the bottom level of the heap.
            {
                CalculateFirstOpenDepth();
                InsertRecursive(node, rootNode, 1);
            }

            numNodes++;
            CalculateDepth();
        }

        //A helper method for Insert() that returns true on a successful insert and false if the node cannot be inserted at the specified depth.
        private bool InsertRecursive(HeapNode nodeToInsert, HeapNode currentNode, int currentDepth)
        {
            if (currentDepth == (firstOpenDepth - 1))  //If we are currently at the depth directly above the level in which we expect to insert our node.
            {
                if (currentNode.leftChild == null || currentNode.rightChild == null)  //If there is an open child space, insert our node.
                {
                    if (currentNode.leftChild == null)
                        currentNode.leftChild = nodeToInsert;
                    else if (currentNode.rightChild == null)
                        currentNode.rightChild = nodeToInsert;

                    nodeToInsert.parent = currentNode;

                    //Determine if a swap with the node's parent should be conducted, since this is a MinHeap.
                    if (nodeToInsert.fValue < currentNode.fValue)
                        SwapNodes(nodeToInsert, currentNode);

                    return true;
                }
                else  //If we are not able to insert our node at the current depth, we are finished with this branch and start walking back up the tree.
                    return false;
            }
            else  //If we have not yet reached the depth directly above the level in which we expect to insert our node.
            {
                bool leftTreeInsertionSuccess = false, rightTreeInsertionSuccess = false;

                //Attempt to insert the node into the left sub-tree (the left child should always be non-null, but we might as well check anyways to be safe).
                if (currentNode.leftChild != null)
                    leftTreeInsertionSuccess = InsertRecursive(nodeToInsert, currentNode.leftChild, currentDepth + 1);

                //Attempt to insert the node into the right sub-tree (the right child should always be non-null, but we might as well check anyways to be safe).
                if (!leftTreeInsertionSuccess && currentNode.rightChild != null)
                    rightTreeInsertionSuccess = InsertRecursive(nodeToInsert, currentNode.rightChild, currentDepth + 1);

                //Determine if a swap with the node's parent should be conducted to maintain a MinHeap.
                if (leftTreeInsertionSuccess && currentNode.leftChild.fValue < currentNode.fValue)
                    SwapNodes(currentNode.leftChild, currentNode);
                else if (rightTreeInsertionSuccess && currentNode.rightChild.fValue < currentNode.fValue)
                    SwapNodes(currentNode.rightChild, currentNode);

                return (leftTreeInsertionSuccess || rightTreeInsertionSuccess);
            }
        }

        //Returns the root of the heap (null is returned if the heap was empty).
        public HeapNode RemoveRoot()
        {
            HeapNode originalRoot = rootNode;

            if (numNodes == 0)  //If our heap is empty, return null.
                originalRoot = null;
            else  //If our heap was not empty, replace the root of the heap with the last element on the last level and rebalance the heap.
            {
                RemoveRootRecursive(rootNode, 1);

                numNodes--;
                CalculateDepth();

                RebalanceHeapTravelingDown(rootNode);
            }

            return originalRoot;
        }

        //A helper method for RemoveRoot().  Finds the rightmost element at the lowest depth, removes it, and places it at the root.
        private bool RemoveRootRecursive(HeapNode currentNode, int currentDepth)
        {
            if (currentDepth == depth)  //The first node that we come across at our lowest depth will be our rightmost node (due to post-order traversal of the heap).
            {
                if (currentNode.parent != null)  //If the bottom-right-most node is not the root.
                {
                    //Sever our current node from the bottom of the heap (by cutting off its parent's reference to it).
                    if (currentNode.parent.leftChild == currentNode)
                        currentNode.parent.leftChild = null;
                    else if (currentNode.parent.rightChild == currentNode)
                        currentNode.parent.rightChild = null;

                    //Make the severed node the new root.
                    currentNode.leftChild = rootNode.leftChild;
                    currentNode.rightChild = rootNode.rightChild;
                    if (currentNode.leftChild != null)
                        currentNode.leftChild.parent = currentNode;
                    if (currentNode.rightChild != null)
                        currentNode.rightChild.parent = currentNode;
                    currentNode.parent = null;
                    rootNode = currentNode;
                }
                else  //The tree only contains a root.
                    rootNode = null;

                return true;
            }
            else  //If we have not yet reached our lowest depth, continue to traverse down the heap.
            {
                bool removedFromRight = false, removedFromLeft = false;
                if (currentNode.rightChild != null)
                    removedFromRight = RemoveRootRecursive(currentNode.rightChild, currentDepth + 1);
                if (!removedFromRight && currentNode.leftChild != null)
                    removedFromLeft = RemoveRootRecursive(currentNode.leftChild, currentDepth + 1);

                return (removedFromRight || removedFromLeft);
            }
        }

        //Swaps two nodes and updates all pointers as needed to maintain the tree structure.
        public void SwapNodes(HeapNode childNodeToSwap, HeapNode parentNodeToSwap)
        {
            bool parentNodeToSwapIsRoot = (parentNodeToSwap == rootNode);
            HeapNode parentNodeToSwapOriginalLeftChild = parentNodeToSwap.leftChild;
            HeapNode parentNodeToSwapOriginalRightChild = parentNodeToSwap.rightChild;
            HeapNode parentNodeToSwapOriginalParent = parentNodeToSwap.parent;

            //Set the parent node's pointers to point to what the child node's were previously pointing to.
            parentNodeToSwap.leftChild = childNodeToSwap.leftChild;
            parentNodeToSwap.rightChild = childNodeToSwap.rightChild;
            if (childNodeToSwap.leftChild != null)
                childNodeToSwap.leftChild.parent = parentNodeToSwap;
            if (childNodeToSwap.rightChild != null)
                childNodeToSwap.rightChild.parent = parentNodeToSwap;

            childNodeToSwap.parent = parentNodeToSwap.parent;

            //Update the child node's pointers to point to the parent node and the parent node's old other child.
            if (childNodeToSwap == parentNodeToSwapOriginalLeftChild)
            {
                childNodeToSwap.leftChild = parentNodeToSwap;
                parentNodeToSwap.parent = childNodeToSwap;
                childNodeToSwap.rightChild = parentNodeToSwapOriginalRightChild;
                if (parentNodeToSwapOriginalRightChild != null)
                    parentNodeToSwapOriginalRightChild.parent = childNodeToSwap;
            }
            else
            {
                childNodeToSwap.leftChild = parentNodeToSwapOriginalLeftChild;
                if (parentNodeToSwapOriginalLeftChild != null)
                    parentNodeToSwapOriginalLeftChild.parent = childNodeToSwap;
                childNodeToSwap.rightChild = parentNodeToSwap;
                parentNodeToSwap.parent = childNodeToSwap;
            }

            if (parentNodeToSwapOriginalParent == null && parentNodeToSwapIsRoot)  //If the root is being swapped with one of its children.
                rootNode = childNodeToSwap;
            else  //We must update the parent node to swap's old parent so it correctly identifies its child as the child node to swap.
            {
                if (parentNodeToSwapOriginalParent.leftChild == parentNodeToSwap)
                    parentNodeToSwapOriginalParent.leftChild = childNodeToSwap;
                else if (parentNodeToSwapOriginalParent.rightChild == parentNodeToSwap)
                    parentNodeToSwapOriginalParent.rightChild = childNodeToSwap;
            }
        }

        //Called from the RemoveRoot() method.  Swaps child and parent nodes, traveling down from the root to potentially a leaf.  This is used when
        //you have a node with an fValue that is too large for its position.
        private void RebalanceHeapTravelingDown(HeapNode currentNode)
        {
            if (currentNode != null)
            {
                bool leftChildMin = false;
                if (currentNode.rightChild == null)
                {
                    if (currentNode.leftChild == null)  //No children exist.
                        return;
                    else  //Only a left child exists.
                        leftChildMin = true;
                }
                else if (currentNode.leftChild != null)  //If both a left and right child exist.
                    leftChildMin = (currentNode.leftChild.fValue < currentNode.rightChild.fValue);
                else  //We should never hit this case (where only a right child exists).
                    return;

                //Swap the current and child nodes if the child has a smaller fValue than the parent.
                if (leftChildMin && (currentNode.fValue > currentNode.leftChild.fValue))
                {
                    SwapNodes(currentNode.leftChild, currentNode);
                    RebalanceHeapTravelingDown(currentNode);
                }
                else if (!leftChildMin && currentNode.fValue > currentNode.rightChild.fValue)
                {
                    SwapNodes(currentNode.rightChild, currentNode);
                    RebalanceHeapTravelingDown(currentNode);
                }
            }
        }

        //Swaps child and parent nodes, traveling up from the inputted node to potentially the root.  This is used when
        //you have a node with an fValue that is too small for its position.
        public void RebalanceHeapTravelingUp(HeapNode currentNode)
        {
            //Swap the parent and current nodes if the current has a smaller fValue than the parent.
            if (currentNode != rootNode && currentNode.fValue < currentNode.parent.fValue)
            {
                SwapNodes(currentNode, currentNode.parent);
                RebalanceHeapTravelingUp(currentNode);
            }
            //Else, your node is in the correct position.
        }

        //The depth of the tree is 0 if there is only a root node, and -1 if not even a root node exists.
        //Since this MinHeap is binary and organized, we can calculate what depth it must have mathematically.
        //This method does not return anything, but updates the class-wide variable "depth".
        private void CalculateDepth()
        {
            if (numNodes == 0)
                depth = -1;
            else
                depth = (int)Math.Floor(Math.Log(numNodes, 2.0) + 1.0);
        }

        //Mathematically calculates the first depth that has an open space for a node.
        private void CalculateFirstOpenDepth()
        {
            CalculateDepth();
            bool fullHeap = (numNodes == (Math.Pow(2, depth) - 1));
            firstOpenDepth = (fullHeap) ? (int)(depth + 1) : (int)(depth);
        }

        //Returns whether the heap is empty.
        public bool isEmpty()
        {
            if (rootNode == null)
                return true;
            else
                return false;
        }
    }
}




//For completeness, here I've transcribed a function called above that is found in another class.
namespace CharacterPlatformer
{
    static class Collision
    {
        //Returns tile coordinates (ranging from [0, 0] to (40, 30)), and takes in on-screen coordinates (ranging from [0, 0] to (1280, 720)).
        //Tile.Width is a const defined to be 32, and Tile.Height is a const defined to be 24.
        public static Vector2 FindWhatTileOnScreenCoordsAreIn(Vector2 coordinates)
        {
            return new Vector2((float)Math.Floor((double)coordinates.X / (double)Tile.Width), (float)Math.Floor((double)coordinates.Y / (double)Tile.Height));
        }
    }
}