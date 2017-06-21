/*
 * The following code is a large, miscellaneous collection of collision-related utility functions programmed
 * in C# with XNA, used primarily to determine whether two rotated sprites are intersecting on a 
 * bounding-box or per-pixel basis.  Some experimental methods for detecting collision scenarios are included.
 * More specialized functions were created as needed to improve performance or accuracy.
 */

namespace CharacterPlatformer
{
    //This class handles much of the collision work of the game.  Individual character classes handle the characters' reactions
    //to collisions, but it is here that the game determines if a collision exists.
    static class Collision
    {
        //Intelligently finds the sprite's bounding rectangle, taking into account rotation origin and angle, but not pixel transparency.
        //The smallest bounding box (made up of vertical and horizontal sides) that surrounds the sprite on-screen is calculated and returned.
        //This method does not find the sprite's bounding rectangle on a per-pixel basis, but rather only uses the four corners of the rotated
        //sprite, so the next step is to run a more intensive per-pixel collision check on anything touching the bounding rectangle.
        public static Rectangle FindBoundingRectangle(Sprite sprite)
        {
            if (sprite.GetRotation() == 0)  //Don't do all the calculations for rotation if we don't need to.
                return new Rectangle(Convert.ToInt32(sprite.position.X), Convert.ToInt32(sprite.position.Y), sprite.sourceRectangle.Width, sprite.sourceRectangle.Height);
            else
            {
                float rotationInRadians = MathHelper.ToRadians(sprite.GetRotation());
                double sineRotationAngle = Math.Sin(rotationInRadians);
                double cosineRotationAngle = Math.Cos(rotationInRadians);

                //Represents the point to rotate as it is displayed on the screen (with the screen position taken into account, not like (0, 0)
                //in the texture) minus the origin as it is displayed on the screen (with the screen position taken into account as well).
                Vector2 rotationVector;

                rotationVector = -sprite.rotationOrigin; //Upper-left corner
                Vector2 upperLeft = (sprite.position + sprite.rotationOrigin) +
                    new Vector2((float)(rotationVector.X * cosineRotationAngle - rotationVector.Y * sineRotationAngle),
                    (float)(rotationVector.X * sineRotationAngle + rotationVector.Y * cosineRotationAngle));

                rotationVector = new Vector2(sprite.sourceRectangle.Width - sprite.rotationOrigin.X, -sprite.rotationOrigin.Y); //Upper-right corner
                Vector2 upperRight = (sprite.position + sprite.rotationOrigin) +
                    new Vector2((float)(rotationVector.X * cosineRotationAngle - rotationVector.Y * sineRotationAngle),
                    (float)(rotationVector.X * sineRotationAngle + rotationVector.Y * cosineRotationAngle));

                rotationVector = new Vector2(sprite.sourceRectangle.Width - sprite.rotationOrigin.X, sprite.sourceRectangle.Height - sprite.rotationOrigin.Y); //Lower-right corner
                Vector2 lowerRight = (sprite.position + sprite.rotationOrigin) +
                    new Vector2((float)(rotationVector.X * cosineRotationAngle - rotationVector.Y * sineRotationAngle),
                    (float)(rotationVector.X * sineRotationAngle + rotationVector.Y * cosineRotationAngle));

                rotationVector = new Vector2(-sprite.rotationOrigin.X, sprite.sourceRectangle.Height - sprite.rotationOrigin.Y); //Lower-left corner
                Vector2 lowerLeft = (sprite.position + sprite.rotationOrigin) +
                    new Vector2((float)(rotationVector.X * cosineRotationAngle - rotationVector.Y * sineRotationAngle),
                    (float)(rotationVector.X * sineRotationAngle + rotationVector.Y * cosineRotationAngle));

                float minX = Math.Min(Math.Min(upperLeft.X, upperRight.X), Math.Min(lowerLeft.X, lowerRight.X));
                float minY = Math.Min(Math.Min(upperLeft.Y, upperRight.Y), Math.Min(lowerLeft.Y, lowerRight.Y));

                float maxX = Math.Max(Math.Max(upperLeft.X, upperRight.X), Math.Max(lowerLeft.X, lowerRight.X));
                float maxY = Math.Max(Math.Max(upperLeft.Y, upperRight.Y), Math.Max(lowerLeft.Y, lowerRight.Y));

                return new Rectangle(Convert.ToInt32(minX), Convert.ToInt32(minY), Convert.ToInt32(maxX - minX), Convert.ToInt32(maxY - minY));
            }
        }


        //Detects whether two sprites collide on a bounding box basis, taking into account rotation and such.
        public static bool DoSpritesCollideBB(Sprite sprite1, Sprite sprite2)
        {
            Rectangle boundingRectangle1 = FindBoundingRectangle(sprite1);
            Rectangle boundingRectangle2 = FindBoundingRectangle(sprite2);

            if (boundingRectangle1.Intersects(boundingRectangle2))
                return true;
            else
                return false;
        }


        //This method determines whether the specified player and projectile collide with one another on a bounding
        //box and, if so, per-pixel basis. Then, the appropriate handlers are called for the player and projectile 
        //to allow for unique reactions from both of them.
        public static bool CollisionPlayerProjectileBBPP(Character player, Matrix? playerMatrix, Projectile projectile, Matrix? projectileMatrix)
        {
            bool projectileDespawn = false;

            if (!projectileMatrix.HasValue)
                projectileMatrix = Collision.FindTransformationMatrix(projectile.projectileSprite);
            if (!playerMatrix.HasValue)
                playerMatrix = Collision.FindTransformationMatrix(player.characterSprite);

            //Check whether the bounding boxes of the two entities collide.
            if (Collision.DoSpritesCollideBB(projectile.projectileSprite, player.characterSprite))
            {
                //The bounding boxes do collide, so check to see if the sprites collide on a per-pixel basis.
                Dictionary<Direction, int> playerProjectileCollision = Collision.PerPixelCollisionsByQuadrant(player.characterSprite,
                    playerMatrix.Value, projectile.projectileSprite, projectileMatrix.Value);

                Direction playerSideCollidedWith = Direction.None;

                int maxCollisions = 0;
                foreach(Direction direction in System.Enum.GetValues(typeof(Direction)))
                    if (playerProjectileCollision[direction] != null && playerProjectileCollision[direction] > maxCollisions)
                    {
                        maxCollisions = playerProjectileCollision[direction];
                        playerSideCollidedWith = direction;
                    }

                if (playerSideCollidedWith != Direction.None)  //If there is a per-pixel collision.
                {
                    projectileDespawn = projectile.CollidedWithPlayer(player, playerSideCollidedWith);
                    player.CollidedWithProjectile(projectile, playerSideCollidedWith);
                }
            }

            return projectileDespawn;
        }


        //This method determines whether the player is colliding with a tile, bounding box-wise and if so, per-pixel.
        //The individual player's CollidedWithTile() method is called for every tile that it intersects to allow for
        //unique reactions to colliding with a tile.
        public static void CollisionPlayerTileBBPP(Character player)
        {
            bool isValidPosition = true;  //Will remain true if no collisions are detected.

            if (player.interactsWithTiles)  //Don't even check for tile collisions if the player is set to not interact with tiles.
            {
                //Call resource-intensive calculations early so they don't need to be performed multiple times.
                Rectangle playerBoundingRectangle = FindBoundingRectangle(player.characterSprite);
                Matrix playerMatrix = FindTransformationMatrix(player.characterSprite);

                //Check to see which tiles this player's bounding rectangle intersects.  Tiles are 32x24.
                //If scrolling is implemented, the position of the camera will have to be taken into account.
                int startingTileX = Convert.ToInt32(Math.Truncate((double)(playerBoundingRectangle.Left / 32)));
                int endingTileX = Convert.ToInt32(Math.Ceiling((double)(playerBoundingRectangle.Right / 32)));
                int startingTileY = Convert.ToInt32(Math.Truncate((double)(playerBoundingRectangle.Top / 24)));
                int endingTileY = Convert.ToInt32(Math.Ceiling(((double)(playerBoundingRectangle.Bottom / 24))));


                //For each tile that the player's sprite potentially collides with:
                for (int x = startingTileX; x <= endingTileX; x++)
                {
                    for (int y = startingTileY; y <= endingTileY; y++)
                    {
                        //If the tile exists (i.e. if the space where the tile could be is not off the edge of the playing field):
                        if (x < Level.tiles.GetLength(0) && y < Level.tiles.GetLength(1) && x >= 0 && y >= 0)
                        {
                            bool collidesWithTile = TileIsConsideredImpassableSurfaceToSprite(player.characterSprite, new Vector2(x, y));

                            //If the player's sprite is colliding with an impassible tile or moving downward and colliding with a platform tile
                            //(platform tiles can be moved up through but not down through) on a bounding box basis...
                            if (collidesWithTile)
                            {
                                //Check if there is really a collision on a per-pixel basis.
                                Dictionary<Direction, int> numCollisionsByQuadrant = PerPixelCollisionsByQuadrant(Level.tiles[x, y].theTile, Level.tiles[x, y].tileMatrix, player.characterSprite, playerMatrix);

                                //Find out which section had the most amount of collisions.  This is an experimental method of collision detection, as opposed to comparing
                                //the positions of the centers of each sprite.
                                int highestFound = 0; //Highest number of collisions in any valid section.
                                int totalHighestFound = 0; //Highest number of collisions in any section.
                                Direction quadrantWithHighest = Direction.None;
                                foreach(Direction direction in System.Enum.GetValues(typeof(Direction)))
                                    if (numCollisionsByQuadrant[direction] != null && numCollisionsByQuadrant[direction] > highestFound)
                                    {
                                        /* Ignore a section if there is no way for the player to collide with it given the positioning 
                                         * of impassable tiles around it.  We only need to account for platform (not impassible) tiles 
                                         * when it has been detected that you're colliding with one.  You still can't collide with the
                                         * right or left sides of the platforms if you are standing on a long stretch of them. 
										 * You can ignore platforms if you're not above the tops of them and therefore can't collide with 
										 * them--collisions should still be detected on the left and right sides of impassible tiles that
										 * border platforms if you're still moving up through them.  This method helps weed out erroneous
                                         * collision reactions. */
                                        if (Level.tiles[x, y].collision == TileCollision.Impassable)
                                        {
                                            //Ignore right, top, bottom, and left-side collisions if an impassible tile is on that side, or if a platform
                                            //(not impassible tile) is there that you're also colliding with.
                                            if (direction == Direction.Top && y - 1 >= 0 && Level.tiles[x, y - 1].collision != TileCollision.Impassable ||
                                                direction == Direction.Right && x + 1 < Level.tiles.GetLength(0) && !(Level.tiles[x + 1, y].collision == TileCollision.Impassable || (Level.tiles[x + 1, y].collision == TileCollision.Platform && TileIsConsideredImpassableSurfaceToSprite(player.characterSprite, new Vector2(x + 1, y)))) ||
                                                direction == Direction.Bottom && y + 1 < Level.tiles.GetLength(1) && Level.tiles[x, y + 1].collision != TileCollision.Impassable ||
                                                direction == Direction.Left && x - 1 >= 0 && !(Level.tiles[x - 1, y].collision == TileCollision.Impassable || (Level.tiles[x - 1, y].collision == TileCollision.Platform && TileIsConsideredImpassableSurfaceToSprite(player.characterSprite, new Vector2(x - 1, y)))))
                                            {
                                                quadrantWithHighest = direction;
                                                highestFound = numCollisionsByQuadrant[direction];
                                                totalHighestFound = highestFound;
                                            }
                                            else
                                            {
                                                totalHighestFound = numCollisionsByQuadrant[direction];

                                                //No valid section has been found yet.  Instead of passing in Direction.None to the player's
                                                //CollidedWithTile() method if no valid collisions in the tile are found, try to tentatively
                                                //guess which side the player's sprite should have collided with to account for the error.
                                                if (quadrantWithHighest == Direction.None)
                                                {
                                                    if (direction == Direction.Top || direction == Direction.Bottom)
                                                    {
                                                        //The tile's center is to the left of the center of the player's sprite.
                                                        if (Level.tiles[x, y].theTile.GetCenter().X < player.characterSprite.GetCenter().X &&
                                                            (x + 1 < Level.tiles.GetLength(0) && !(Level.tiles[x + 1, y].collision == TileCollision.Impassable ||
                                                            (Level.tiles[x + 1, y].collision == TileCollision.Platform && TileIsConsideredImpassableSurfaceToSprite(player.characterSprite, new Vector2(x + 1, y))))))
                                                            quadrantWithHighest = Direction.Right;
                                                        //The tile's center is to the right of the center of the player's sprite.
                                                        else if (Level.tiles[x, y].theTile.GetCenter().X > player.characterSprite.GetCenter().X &&
                                                            (x - 1 >= 0 && !(Level.tiles[x - 1, y].collision == TileCollision.Impassable ||
                                                            (Level.tiles[x - 1, y].collision == TileCollision.Platform && TileIsConsideredImpassableSurfaceToSprite(player.characterSprite, new Vector2(x - 1, y))))))
                                                            quadrantWithHighest = Direction.Left;
                                                    }
                                                    else if (direction == Direction.Right || direction == Direction.Left)
                                                    {
                                                        //The tile's center is above the center of the player's sprite.
                                                        if (Level.tiles[x, y].theTile.GetCenter().Y < player.characterSprite.GetCenter().Y &&
                                                            (y + 1 < Level.tiles.GetLength(1) && Level.tiles[x, y + 1].collision != TileCollision.Impassable))
                                                            quadrantWithHighest = Direction.Bottom;
                                                        //The tile's center is below the center of the player's sprite.
                                                        else if (Level.tiles[x, y].theTile.GetCenter().Y > player.characterSprite.GetCenter().Y &&
                                                            (y - 1 >= 0 && Level.tiles[x, y - 1].collision != TileCollision.Impassable))
                                                            quadrantWithHighest = Direction.Top;
                                                    }
                                                }
                                            }
                                        }
                                        else if (Level.tiles[x, y].collision == TileCollision.Platform)  //Platforms may be moved up through but not down through, so they require slightly different code here.
                                        {
                                            if (direction == Direction.Top && y - 1 >= 0 && Level.tiles[x, y - 1].collision != TileCollision.Impassable ||
                                                direction == Direction.Right && x + 1 < Level.tiles.GetLength(0) && Level.tiles[x + 1, y].collision != TileCollision.Impassable && Level.tiles[x + 1, y].collision != TileCollision.Platform ||
                                                direction == Direction.Bottom && y + 1 < Level.tiles.GetLength(1) && Level.tiles[x, y + 1].collision != TileCollision.Impassable ||
                                                direction == Direction.Left && x - 1 >= 0 && Level.tiles[x - 1, y].collision != TileCollision.Impassable && Level.tiles[x - 1, y].collision != TileCollision.Platform)
                                            {
                                                quadrantWithHighest = direction;
                                                highestFound = numCollisionsByQuadrant[direction];
                                                totalHighestFound = highestFound;
                                            }
                                            else
                                            {
                                                totalHighestFound = numCollisionsByQuadrant[direction];

                                                //No valid section has been found yet.  Instead of passing in Direction.None to the player's
                                                //CollidedWithTile() method if no valid collisions in the tile are found, try to tentatively
                                                //guess which side the player's sprite should have collided with to account for the error.
                                                if (quadrantWithHighest == Direction.None)
                                                {
                                                    if (direction == Direction.Top || direction == Direction.Bottom)
                                                    {
                                                        //The tile's center is to the left of the center of the player's sprite.
                                                        if (Level.tiles[x, y].theTile.GetCenter().X < player.characterSprite.GetCenter().X &&
                                                            (x + 1 < Level.tiles.GetLength(0) && Level.tiles[x + 1, y].collision != TileCollision.Impassable &&
                                                            Level.tiles[x + 1, y].collision != TileCollision.Platform))
                                                            quadrantWithHighest = Direction.Right;
                                                        //The tile's center is to the right of the center of the player's sprite.
                                                        else if (Level.tiles[x, y].theTile.GetCenter().X > player.characterSprite.GetCenter().X &&
                                                            (x - 1 >= 0 && Level.tiles[x - 1, y].collision != TileCollision.Impassable && Level.tiles[x - 1, y].collision != TileCollision.Platform))
                                                            quadrantWithHighest = Direction.Left;
                                                    }
                                                    else if (direction == Direction.Right || direction == Direction.Left)
                                                    {
                                                        //The tile's center is above the center of the player's sprite.
                                                        if (Level.tiles[x, y].theTile.GetCenter().Y < player.characterSprite.GetCenter().Y &&
                                                        (y + 1 < Level.tiles.GetLength(1) && Level.tiles[x, y + 1].collision != TileCollision.Impassable))
                                                            quadrantWithHighest = Direction.Bottom;
                                                        //The tile's center is below the center of the player's sprite.
                                                        else if (Level.tiles[x, y].theTile.GetCenter().Y > player.characterSprite.GetCenter().Y &&
                                                        (y - 1 >= 0 && Level.tiles[x, y - 1].collision != TileCollision.Impassable))
                                                            quadrantWithHighest = Direction.Top;
                                                    }
                                                }
                                            }
                                        }
                                    }

                                //If there is a clean collision or there are only invalid collisions (when collisions are detected with 
                                //tiles that we know the sprite should not be able to reach (such as when the tile is bounded by other tiles).
                                if (quadrantWithHighest != Direction.None || (quadrantWithHighest == Direction.None && totalHighestFound > 0))
                                {
                                    if(isValidPosition) //For the first collision you detect, move the sprite to its previous (valid) location and rotation.
                                    {
                                        isValidPosition = false;
                                        player.characterSprite.position = player.characterSprite.lastValidPosition;
                                        player.characterSprite.SetRotation(player.characterSprite.lastValidRotation);
                                    }

                                    //Have the player perform its specific reaction to colliding with a tile.
                                    player.CollidedWithTile(Level.tiles[x, y], new Vector2(x, y), quadrantWithHighest);
                                }
                            }
                        }
                    }
                }
            }

            if (!isValidPosition)
            {
                player.isValidPositionCurrentFrame = false;

                //If you're still colliding with the side of the screen or another tile, after you've been edged away from tiles in
                //player.CollidedWithTile().  Used only as a failsafe.
                if (DoesCollideWithAnyTileBBPP(player.characterSprite) ||
                    DoesCollideWithScreenEdgeBBPP(player.characterSprite))
                {
                    player.characterSprite.position = new Vector2(player.characterSprite.lastValidPosition.X, player.characterSprite.lastValidPosition.Y);
                }
            }
        }


        //This method determines the tiles with which the specified sprite collides, bounding box-wise and, if so, per-pixel.
        //tileType is a nullable parameter which specifies which type (passable, platform, etc.) of tile you are
        //interested in finding collisions with. In the case that tileType is null, a list of all tiles with which
        //theSprite collides will be returned, irrespective of type.  Platform tiles that the character is traveling up though
        //are included in the returned list.
        public static List<Tile> FindCollisionTileListBBPP(Sprite theSprite, TileCollision? tileType)
        {
            Rectangle spriteBoundingRectangle = FindBoundingRectangle(theSprite);
            Matrix spriteMatrix = FindTransformationMatrix(theSprite);
            List<Tile> collidedTiles = new List<Tile>();

            //Check to see which tiles this player's bounding rectangle intersects.  Tiles are 32x24.
            //If scrolling is implemented, the position of the camera will have to be taken into account.
            int startingTileX = Convert.ToInt32(Math.Truncate((double)(spriteBoundingRectangle.Left / 32)));
            int endingTileX = Convert.ToInt32(Math.Ceiling((double)(spriteBoundingRectangle.Right / 32)));  //Effectively rounds up.
            int startingTileY = Convert.ToInt32(Math.Truncate((double)(spriteBoundingRectangle.Top / 24)));
            int endingTileY = Convert.ToInt32(Math.Ceiling(((double)(spriteBoundingRectangle.Bottom / 24))));

            //For each tile that the sprite's bounding box collides with:
            for (int x = startingTileX; x <= endingTileX; x++)
            {
                for (int y = startingTileY; y <= endingTileY; y++)
                {
                    //If the tile type matches tileType and the tile is not off the edge of the playing field:
                    if ((x < Level.tiles.GetLength(0) && y < Level.tiles.GetLength(1) && x >= 0 && y >= 0) &&
                        (!tileType.HasValue || (Level.tiles[x, y].collision == tileType.Value)))
                    {
                        //Check to see if there is really a collision on a per-pixel basis.
                        Dictionary<Direction, int> numCollisionsByQuadrant = PerPixelCollisionsByQuadrant(Level.tiles[x, y].theTile, Level.tiles[x, y].tileMatrix, theSprite, spriteMatrix);
                        if (numCollisionsByQuadrant[Direction.Top] > 0 || numCollisionsByQuadrant[Direction.Right] > 0 || numCollisionsByQuadrant[Direction.Bottom] > 0 || numCollisionsByQuadrant[Direction.Left] > 0)
                            collidedTiles.Add(Level.tiles[x, y]);
                    }
                }
            }
            return collidedTiles;
        }


        //Called after bounding box collision detection thinks the sprite is colliding with the tile.  Checks whether
        //there is really a bounding box collision based on whether the tile is a platform tile or impassible tile, and if the 
        //sprite is moving downward ("platform" tiles may be moved up through but not down through).
        public static bool TileIsConsideredImpassableSurfaceToSprite(Sprite sprite, Vector2 tileCoords)
        {
            bool collidesWithTile = false;
            int x = Convert.ToInt32(tileCoords.X);
            int y = Convert.ToInt32(tileCoords.Y);

            //No reason to check for collision if the tile is passable; the method returns false in that case.
            //For platforms, collision is found if the player's sprite is moving downwards, and last frame the bottom 
            //of the player's sprite was above the top of the platform tile.
            if (Level.tiles[x, y].collision == TileCollision.Impassable)
                collidesWithTile = true;
            else if (Level.tiles[x, y].collision == TileCollision.Platform)
            {
                //If last frame, the bottom of the sprite was above the top of the platform.
                if (LastValidBottomMostPixelIsAboveYCoordinatePP(sprite, Level.tiles[x, y].theTile.position.Y))
                {
                    //This covers if the sprite is moving downward, as well as if the sprite is moving upwards but
                    //rotates such that part of the sprite is now inside the platform.
                    collidesWithTile = true;
                }
            }

            return collidesWithTile;
        }


        /* The textures are 2D arrays of Colors, and the Matrices represent the rotation and position of the texture.
         * This function returns an dictionary indicating the number of collisions detected in each quadrant of sprite1.  
         * This works on a per-pixel basis and should therefore be called when it is suspected that the two textures 
         * collide based on a bounding rectangle intersection.  For the time being, the two textures are considered 
         * to collide if any two non-transparent pixels are on top of one another. */
        public static Dictionary<Direction, int> PerPixelCollisionsByQuadrant(Sprite sprite1, Matrix matrix1, Sprite sprite2, Matrix matrix2)
        {
            //Iterate through the smaller sprite's pixel matrix when performing collision detection
            //because a smaller number of pixel transformations may need to occur.
            bool spritesSwapped = false;
            if (sprite1.GetDiagonalLength() > sprite2.GetDiagonalLength())
            {
                Sprite swapSprite = sprite2;
                sprite2 = sprite1;
                sprite1 = swapSprite;

                Matrix swapMatrix = matrix2;
                matrix2 = matrix1;
                matrix1 = swapMatrix;

                spritesSwapped = true;
            }

            //Transforming a coordinate from texture1 using the following matrix will give us the corresponding coordinate in texture2!
            Matrix matrix1ToMatrix2 = matrix1 * Matrix.Invert(matrix2);

            int widthTexture1 = sprite1.sourceRectangle.Width;
            int heightTexture1 = sprite1.sourceRectangle.Height;
            int widthTexture2 = sprite2.sourceRectangle.Width;
            int heightTexture2 = sprite2.sourceRectangle.Height;

            Dictionary<Direction, int> numCollisionsByQuadrant = new Dictionary<Direction, int>();
            foreach(Direction direction in System.Enum.GetValues(typeof(Direction)))
                numCollisionsByQuadrant[direction] = 0;

            //Iterate through each pixel.
            for (int currentWidthTexture1 = sprite1.sourceRectangle.X; currentWidthTexture1 < widthTexture1; currentWidthTexture1++)
            {
                for (int currentHeightTexture1 = sprite1.sourceRectangle.Y; currentHeightTexture1 < heightTexture1; currentHeightTexture1++)
                {
                    //If the current pixel within the first sprite is non-transparent, continue processing.
                    if (sprite1.pixelColors[sprite1.frame, currentWidthTexture1, currentHeightTexture1].A > 0)
                    {
                        Vector2 coordinateInTexture1 = new Vector2(currentWidthTexture1, currentHeightTexture1);

                        //This transformation determines which pixel in texture2 corresponds with the pixel in texture1.
                        Vector2 coordinateInTexture2 = Vector2.Transform(coordinateInTexture1, matrix1ToMatrix2);

                        //Checks to see if the pixel from texture1 corresponds with a pixel in texture2 at all.
                        if ((int)coordinateInTexture2.X >= sprite2.sourceRectangle.X && (int)coordinateInTexture2.X < widthTexture2 &&
                            (int)coordinateInTexture2.Y >= sprite2.sourceRectangle.Y && (int)coordinateInTexture2.Y < heightTexture2)
                        {
                            //If the current pixel within the second sprite is non-transparent, we have a collision.
                            if (sprite2.pixelColors[sprite2.frame, (int)coordinateInTexture2.X, (int)coordinateInTexture2.Y].A > 0)
                            {
                                //Find out what section the collision occurred in (top, right, bottom, or left).
                                //Ratios are used because not all sprites are squares, so you can't directly compare pixel positions.
                                float widthRatio;
                                float heightRatio;
                                if (!spritesSwapped)
                                {
                                    widthRatio = (float)currentWidthTexture1 / (float)sprite1.sourceRectangle.Width;
                                    heightRatio = (float)currentHeightTexture1 / (float)sprite1.sourceRectangle.Height;
                                }
                                else
                                {
                                    widthRatio = coordinateInTexture2.X / (float)sprite2.sourceRectangle.Width;
                                    heightRatio = coordinateInTexture2.Y / (float)sprite2.sourceRectangle.Height;
                                }

                                if (widthRatio >= heightRatio && 1 - widthRatio >= heightRatio) //Upper quadrant.
                                    numCollisionsByQuadrant[Direction.Top]++;
                                else if (1 - widthRatio <= heightRatio && widthRatio >= heightRatio) //Right quadrant.
                                    numCollisionsByQuadrant[Direction.Right]++;
                                else if (heightRatio >= widthRatio && 1 - widthRatio <= heightRatio) //Bottom quadrant.
                                    numCollisionsByQuadrant[Direction.Bottom]++;
                                else if (1 - widthRatio >= heightRatio && heightRatio >= widthRatio) //Left quadrant.
                                    numCollisionsByQuadrant[Direction.Left]++;
                            }
                        }
                    }
                }
            }
            return numCollisionsByQuadrant;
        }


        #region Bottom-most Pixel Detection

        //Returns the on-screen (x, y) coordinate of the bottom-most pixel in the transformed sprite, taking into account position,
        //rotation angle, and origin.  (-1, -1) is returned if no pixels with an alpha value greater than zero are found (it is 
        //possible for the lowest pixel found to be (-1, -1), but this would mean the sprite is completely off the screen and any further 
        //per-pixel collision checks would not find any collisions).
        //This method was intended to help determine whether the sprite can collide with a platform tile (platforms can be moved up through
        //but not down through) by comparing the locations of the sprite's bottom-most pixel for its current and immediately previous 
        //locations/rotations.  This algorithm helps skirt common bugs like being able to rotate into a platform, but the implementation
        //below transforms each pixel in the sprite, so it is resource-intensive enough that a faster and more specific implementation--
        //BottomMostPixelIsAboveYCoordinatePP()--was created to supersede it.
        public static Vector2 FindBottomMostPixelPP(Sprite sprite)
        {
            //If the sprite is unrotated, don't bother with redundant, computationally expensive calculations.
            if (sprite.GetRotation() == 0f)
            {
                //Iterate through each pixel, starting with the bottom row.
                for (int currentHeightTexture = sprite.sourceRectangle.Y + sprite.sourceRectangle.Height - 1;
                    currentHeightTexture >= sprite.sourceRectangle.Y; currentHeightTexture--)
                {
                    for (int currentWidthTexture = sprite.sourceRectangle.X;
                        currentWidthTexture < sprite.sourceRectangle.Width; currentWidthTexture++)
                    {
                        //If the Alpha value is greater than 0.
                        if (sprite.pixelColors[sprite.frame, currentWidthTexture, currentHeightTexture].A > 0)
                            return new Vector2(sprite.position.X + currentWidthTexture, sprite.position.Y + currentHeightTexture);
                    }
                }
                return new Vector2(-1, -1);
            }
            else
                return FindPixelFurthestInThisDirectionPP(sprite, Direction.Bottom);
        }


        //Returns the on-screen (x, y) coordinate of the pixel the farthest in the inputted direction in the transformed sprite,
        //taking into account position, rotation angle, origin, and transparency.  (-1, -1) is returned in the event of an error, 
        //such as there being no non-transparent pixels.  
        //This method was intended to help determine whether the sprite can collide with a platform tile (platforms can be moved up through
        //but not down through) by comparing the locations of the sprite's bottom-most pixel for its current and immediately previous 
        //locations/rotations.  This algorithm helps skirt common bugs like being able to rotate into a platform, but the implementation
        //below transforms each pixel in the sprite, so it is resource-intensive enough that a faster and more specific implementation--
        //BottomMostPixelIsAboveYCoordinatePP()--was created to supersede it.
        private static Vector2 FindPixelFurthestInThisDirectionPP(Sprite sprite, Direction direction)
        {
            if (direction == Direction.None)  //Invalid direction.
                return new Vector2(-1, -1);

            Matrix transformationMatrix = FindTransformationMatrix(sprite);  //Takes into account position, rotation, and origin.

            Vector2 furthestPixelInDirection = new Vector2(-1, -1);

            //Iterate through each pixel.
            for (int currentWidthTexture = sprite.sourceRectangle.X; currentWidthTexture < sprite.sourceRectangle.Width; currentWidthTexture++)
            {
                for (int currentHeightTexture = sprite.sourceRectangle.Y; currentHeightTexture < sprite.sourceRectangle.Height; currentHeightTexture++)
                {
                    Vector2 coordinateInTexture = new Vector2(currentWidthTexture, currentHeightTexture);

                    //If the Alpha value is greater than 0.
                    if (sprite.pixelColors[sprite.frame, Convert.ToInt32(coordinateInTexture.X), Convert.ToInt32(coordinateInTexture.Y)].A > 0)
                    {
                        //This transformation determines which pixel the current one corresponds to in the transformed matrix.
                        Vector2 coordinateInTextureTransformed = Vector2.Transform(coordinateInTexture, transformationMatrix);

                        if (direction == Direction.Top)
                        {
                            //If the pixel is higher than our current highest (disregard pixels with the same Y-value but different X-values).
                            if (furthestPixelInDirection == null || furthestPixelInDirection.Y == -1 || coordinateInTextureTransformed.Y < furthestPixelInDirection.Y)
                                furthestPixelInDirection = coordinateInTextureTransformed;
                        }
                        else if (direction == Direction.Right)
                        {
                            //If the pixel is more right than our current right-most (disregard pixels with the same X-value but different Y-values).
                            if (furthestPixelInDirection == null || coordinateInTextureTransformed.X > furthestPixelInDirection.X)
                                furthestPixelInDirection = coordinateInTextureTransformed;
                        }
                        else if (direction == Direction.Bottom)
                        {
                            //If the pixel is lower than our current lowest (disregard pixels with the same Y-value but different X-values).
                            if (furthestPixelInDirection == null || coordinateInTextureTransformed.Y > furthestPixelInDirection.Y)
                                furthestPixelInDirection = coordinateInTextureTransformed;
                        }
                        else if (direction == Direction.Left)
                        {
                            //If the pixel is more left than our current left-most (disregard pixels with the same X-value but different Y-values).
                            if (furthestPixelInDirection == null || furthestPixelInDirection.X == -1 || coordinateInTextureTransformed.X < furthestPixelInDirection.X)
                                furthestPixelInDirection = coordinateInTextureTransformed;
                        }
                    }
                }
            }
            return furthestPixelInDirection;
        }


        /* Returns true if the bottom-most pixel in the sprite with an alpha value greater than 0 is above (not equal to) the 
         * inputted screen-space Y-coordinate.  False is returned otherwise or if no pixels with an alpha value greater than zero are found.
         * This method is intended to help to quickly determine whether the sprite can collide with a platform (platforms can be
         * moved up through but not down through, and by comparing the locations of the sprite's bottom-most pixel for its current
         * and immediately previous locations, we can skirt common bugs like being able to rotate into a platform).
         * 
         * This method is a less resource-intensive version of FindBottomMostPixelPP() in that it will avoid doing vector transformations
         * for every pixel in the unrotated sprite to see where each pixel is after being rotated.  For efficiency, the sprite's rotated
         * graphic is separated into a possible rectangle and a triangle or cut-off triangle of pixels.  When doing transformations
         * for every pixel in the sprite like in FindBottomMostPixelPP(), gravity-bound playable characters that can rotate would cause
         * slowdown when standing on "platform" tiles since this check would be performed every frame.
         */
        public static bool BottomMostPixelIsAboveYCoordinatePP(Sprite sprite, float yCoord)
        {
            float currentRotation = sprite.GetRotation();

            if (currentRotation == 0)  //Don't bother with transformations if the sprite is unrotated.
            {
                //Iterate through each pixel, starting with the bottom row.
                for (int currentHeightTexture = sprite.sourceRectangle.Y + sprite.sourceRectangle.Height - 1;
                    currentHeightTexture >= sprite.sourceRectangle.Y; currentHeightTexture--)
                {
                    for (int currentWidthTexture = sprite.sourceRectangle.X;
                        currentWidthTexture < sprite.sourceRectangle.X + sprite.sourceRectangle.Width - 1; currentWidthTexture++)
                    {
                        //If the Alpha value is greater than 0.
                        if (sprite.pixelColors[sprite.frame, currentWidthTexture, currentHeightTexture].A > 0)
                        {
                            if (sprite.position.Y + currentHeightTexture < yCoord) //If the pixel is above the inputted y-value.
                                return true;
                            else
                                return false;  //The first pixel we encounter will be the bottom-most pixel.  If it is above the y-value, we can return false.
                        }
                    }
                }
                return false;
            }
            else  //The sprite is rotated in some way.
            {
                //There are a few special cases where we can avoid transformations entirely to save computation time.
                if (currentRotation == 90)
                    return BottomMostPixelIsAboveYCoordinateHelper90Degrees(sprite, yCoord);
                else if (currentRotation == 180)
                    return BottomMostPixelIsAboveYCoordinateHelper180Degrees(sprite, yCoord);  //Very similar to BottomMostPixelIsAboveYCoordinateHelper90Degrees().
                else if (currentRotation == 270)
                    return BottomMostPixelIsAboveYCoordinateHelper270Degrees(sprite, yCoord);  //Very similar to BottomMostPixelIsAboveYCoordinateHelper90Degrees().


                //The bottom-right corner on the unrotated sprite is the bottom corner on the rotated sprite, 
                //and the top-left corner on the unrotated sprite is the top corner on the rotated sprite, regardless of origin.
                if (currentRotation > 0 && currentRotation < 90)
                    return BottomMostPixelIsAboveYCoordinateHelper0To90Degrees(sprite, yCoord);

                //The top-right corner on the unrotated sprite is the bottom corner on the rotated sprite, 
                //and the bottom-left corner on the unrotated sprite is the top corner on the rotated sprite, regardless of origin.
                else if (currentRotation > 90 && currentRotation < 180)
                    return BottomMostPixelIsAboveYCoordinateHelper90To180Degrees(sprite, yCoord);  //Very similar to BottomMostPixelIsAboveYCoordinateHelper0To90Degrees().

                //The top-left corner on the unrotated sprite is the bottom corner on the rotated sprite, 
                //and the bottom-right corner on the unrotated sprite is the top corner on the rotated sprite, regardless of origin.
                else if (currentRotation > 180 && currentRotation < 270)
                    return BottomMostPixelIsAboveYCoordinateHelper180To270Degrees(sprite, yCoord);  //Very similar to BottomMostPixelIsAboveYCoordinateHelper0To90Degrees().

                //The bottom-left corner on the unrotated sprite is the bottom corner on the rotated sprite, 
                //and the top-right corner on the unrotated sprite is the top corner on the rotated sprite, regardless of origin.
                else if (currentRotation > 270 && currentRotation < 360)
                    return BottomMostPixelIsAboveYCoordinateHelper270To360Degrees(sprite, yCoord);  //Very similar to BottomMostPixelIsAboveYCoordinateHelper0To90Degrees().
            }
        }


        //A helper method for BottomMostPixelIsAboveYCoordinatePP() that handles a special scenario where the sprite is rotated
        //exactly 90 degrees and we therefore know the bottom-most pixel on the rotated sprite will be the right-most pixel on the unrotated sprite.
        //Returns true if the bottom-most pixel in the sprite with an alpha value greater than 0 is above (not equal to) the 
        //inputted Y-coordinate.  False is returned otherwise or if no pixels with an alpha value greater than zero are found.
        private static bool BottomMostPixelIsAboveYCoordinateHelper90Degrees(Sprite sprite, float yCoord)
        {
            //Iterate through each pixel, starting with the rightmost column of the unrotated sprite.
            for (int currentWidthTexture = sprite.sourceRectangle.X + sprite.sourceRectangle.Width - 1;
                    currentWidthTexture >= sprite.sourceRectangle.X; currentWidthTexture--)
            {
                for (int currentHeightTexture = sprite.sourceRectangle.Y + sprite.sourceRectangle.Height - 1;
                    currentHeightTexture >= sprite.sourceRectangle.Y; currentHeightTexture--)
                {
                    //If the Alpha value is greater than 0.
                    if (sprite.pixelColors[sprite.frame, currentWidthTexture, currentHeightTexture].A > 0)
                    {
                        if (sprite.position.Y + (.5 * (sprite.sourceRectangle.Height - sprite.sourceRectangle.Width)) +
                            currentWidthTexture < yCoord) //If the pixel is above the inputted y-value.
                            return true;
                        else
                            return false;  //The first pixel we encounter will be the bottom-most pixel.  If it is above the y-value, we can return false.
                    }
                }
            }
            return false;
        }


        //A helper method for BottomMostPixelIsAboveYCoordinatePP() for sprites that have been rotated somewhere between 0 and 90
        //degrees, so we know that the bottom-right corner on the unrotated sprite is the bottom corner on the rotated sprite, 
        //and the top-left corner on the unrotated sprite is the top corner on the rotated sprite, regardless of origin.
        //Knowing this allows us to skip having to transform many of the sprite's pixels, saving heavily on processing time.
        //Returns true if the bottom-most pixel in the sprite with an alpha value greater than 0 is above (not equal to) the 
        //inputted Y-coordinate.  False is returned otherwise or if no pixels with an alpha value greater than zero are found.
        //This code privately separates the sprite's rotated graphic into a possible rectangle and a triangle or cut-off triangle.
        private static bool BottomMostPixelIsAboveYCoordinateHelper0To90Degrees(Sprite sprite, float yCoord)
        {
            Matrix transformationMatrix = FindTransformationMatrix(sprite);  //Takes into account position, rotation, and origin.
            Vector2 coordinateInTexture = new Vector2();
            Vector2 coordinateInTextureTransformed = new Vector2();

            coordinateInTexture.X = sprite.sourceRectangle.X;
            coordinateInTexture.Y = sprite.sourceRectangle.Bottom - 1;

            /* If the bottom-left corner of the unrotated sprite is above the inputted y-coordinate, we know we need to look along the
               bottom of the sprite and determine the left-most point that is on or beneath the y-coordinate line in the rotated sprite.
               All points below the inputted y-value can be found in a triangle at the bottom-right of the unrotated sprite,
               as shown in the example below, where the inputted y-value line is represented by a series of x's and *'s:


                       Unrotated                          Rotated less than 45 degrees
                                                             ___
                 _____________________                      |   ---___
                |                     |                     |         ---___
                |                     |                    |                ---___
                |                     |     ____ \         |                      |
                |                     |           \       |                       |
                |                     |     ____  /       |                      |
                |                 xx**|          /       |                       |
                |             xx**    |                  |___                   |
                |_________xx**________|                      ---___xxxxxxxxxxxxx|
                                                                   ---___      |
                                                                         ---___|
             */
            if (Convert.ToInt32(Vector2.Transform(coordinateInTexture, transformationMatrix).Y) < Convert.ToInt32(yCoord))
            {
                //An x-value such that (bottomSideMinX, sprite.sourceRectangle.Y) on the unrotated sprite is located below 
                //the inputted y-value, and anything where y = 0 and x < bottomSideMinX (i.e. where X is to the right of 
                //bottomSideMinX on the unrotated sprite) is above the inputted y-value.
                int bottomSideMinX = -1;

                coordinateInTexture.X = sprite.sourceRectangle.Right - 1;
                coordinateInTexture.Y = sprite.sourceRectangle.Bottom - 1;

                bool foundBottomSideMinX = false;

                //Determine the pixel that corresponds with the triangle's vertex along the bottom of the unrotated sprite.
                //To do this, move left from the bottom-right corner of the unrotated sprite, until we find something with alpha > 0 
                //that's below the inputted y-value or something that's above the inputted y-value (in which case, the pixel
                //to the right of it is bottomSideMinX).
                for (int currentWidthTexture = sprite.sourceRectangle.Right - 1;
                    !foundBottomSideMinX && currentWidthTexture >= sprite.sourceRectangle.X; currentWidthTexture--)
                {
                    coordinateInTexture.X = currentWidthTexture;
                    coordinateInTextureTransformed = Vector2.Transform(coordinateInTexture, transformationMatrix);

                    if (coordinateInTextureTransformed.Y < yCoord)  //If the coordinate is above the inputted y-value.
                    {
                        bottomSideMinX = currentWidthTexture + 1;
                        foundBottomSideMinX = true;
                    }
                    else if (sprite.pixelColors[sprite.frame, currentWidthTexture, sprite.sourceRectangle.Bottom - 1].A > 0)
                        return false;  //A non-transparent pixel has been found that is below the inputted y-value.
                }

                //If the bottom-right pixel was above (but not equal to) the inputted y-value.  
                //We then know that nothing is underneath the inputted y-value, so we can return true.
                if (bottomSideMinX >= sprite.sourceRectangle.Right)
                    return true;

                //Now, we have a point on the bottom row of the sprite which, when rotated, is the leftmost point in the
                //bounding box that is on the horizontal line representing the inputted y-coordinate.  If the rightmost
                //point is on the right side of the unrotated sprite, we have to check the pixels in a triangle; if
                //it is on the top of the unrotated sprite, we need to iterate through a cut-off triangle.

                //rightSideMinY is the right side of the unrotated triangle (the height), which is equal to the base
                //(a segment on the bottom-right side) times the tangent of the rotation angle.
                float rightSideMinY = ((sprite.sourceRectangle.Right - 1) - bottomSideMinX) * (float)Math.Tan(MathHelper.ToRadians(sprite.GetRotation()));

                //The slope of the line where y = the inputted y-value.
                float slope = rightSideMinY / (float)bottomSideMinX;
                if (float.IsNaN(slope))  //If the slope is infinite.
                    slope = sprite.sourceRectangle.Height;

                //We are looking at a triangle or a triangle with its tip cut off.
                //Go through all the points contained in this triangle underneath the inputted y-value.
                for (int currentWidthTexture = bottomSideMinX; currentWidthTexture <= sprite.sourceRectangle.Right - 1; currentWidthTexture++)
                {
                    for (int currentHeightTexture = sprite.sourceRectangle.Bottom - 1; currentHeightTexture >= sprite.sourceRectangle.Y &&
                    currentHeightTexture >= Convert.ToInt32(sprite.sourceRectangle.Bottom - 1 - ((currentWidthTexture - bottomSideMinX - sprite.sourceRectangle.X) * slope));
                    currentHeightTexture--)
                    {
                        //If the Alpha value is greater than 0, we've found a non-transparent pixel below the inputted y-value.
                        if (sprite.pixelColors[sprite.frame, currentWidthTexture, currentHeightTexture].A > 0)
                            return false;
                    }
                }

                return true;  //No pixels with an alpha value greater than 0 are beneath the inputted y-value.
            }
            /* Else, the bottom-left corner of the sprite, when rotated, is below the inputted y-value.  The area underneath
               the inputted y-value can be separated into a rectangle at the bottom of the unrotated sprite and a triangle above it,
               like so.  The inputted y-value line is represented by a series of x's and *'s, and the line separating the area
               below into a triangle and a rectangle uses o's and °'s:


                       Unrotated                          Rotated less than 45 degrees
                                                             ___
                 _____________________                      |   ---___
                |                     |                     |         ---___
                |                     |                    |                ---___
                |               xxx***|     ____ \         |                      |
                |         xxx***      |           \       |                       |
                |   xxx***            |     ____  /       |xxxxxxxxxxxxxxxxxxxxxx|
                |***oooooooooooooooooo|          /       |   °°°ooo              |
                |                     |                  |___       °°°ooo      |
                |_____________________|                      ---___       °°°ooo|
                                                                   ---___      |
                                                                         ---___|
            */
            else
            {
                //A y-value such that (sprite.sourceRectangle.X, leftSideMinY) on the unrotated sprite is located below 
                //the inputted y-value, and anything where x = sprite.sourceRectangle.X and y < leftSideMinY (i.e. where 
                //y is above leftSideMinY on the unrotated sprite) is above the inputted y-value.
                int leftSideMinY = -1;

                coordinateInTexture.X = sprite.sourceRectangle.X;
                coordinateInTexture.Y = sprite.sourceRectangle.Bottom - 1;

                bool foundLeftSideMinY = false;

                //Move up from the bottom-left corner of the unrotated sprite, until we find the pixel (transparent or not) 
                //that's above the inputted y-value (in which case, the pixel below it is leftSideMinY).
                for (int currentHeightTexture = sprite.sourceRectangle.Bottom - 1;
                    !foundLeftSideMinY && currentHeightTexture >= sprite.sourceRectangle.Y; currentHeightTexture--)
                {
                    coordinateInTexture.Y = currentHeightTexture;
                    coordinateInTextureTransformed = Vector2.Transform(coordinateInTexture, transformationMatrix);

                    if (coordinateInTextureTransformed.Y < yCoord) //If the coordinate is above the inputted y-value.
                    {
                        leftSideMinY = currentHeightTexture + 1;
                        foundLeftSideMinY = true;
                    }
                    else if (sprite.pixelColors[sprite.frame, sprite.sourceRectangle.X, currentHeightTexture].A > 0)
                        return false;  //A non-transparent pixel has been found that is below the inputted y-value.
                }

                //If the sprite's bounding box is entirely below the inputted y-value.
                if (!foundLeftSideMinY)
                    return false;

                //If the bottom-left pixel was above or equal to the inputted y-value--the above loop terminated in its first pass.
                if (leftSideMinY >= sprite.sourceRectangle.Bottom)
                    leftSideMinY = sprite.sourceRectangle.Bottom - 1;  //Clamp the value so there is not an array index out of bounds error.

                //Now, we have a point on the left side of the sprite which, when rotated, is on the horizontal line
                //representing the inputted y-coordinate.

                //Since anything below leftSideMinY in the unrotated sprite is guaranteed to be below the inputted y-value,
                //we can go through the rectangle formed below leftSideMinY in the unrotated sprite.  This avoids having to
                //do a lot of transformations.
                for (int currentHeightTexture = leftSideMinY;
                    currentHeightTexture <= sprite.sourceRectangle.Bottom - 1; currentHeightTexture++)
                {
                    for (int currentWidthTexture = sprite.sourceRectangle.X;
                        currentWidthTexture < sprite.sourceRectangle.Right - 1; currentWidthTexture++)
                    {
                        //If the Alpha value is greater than 0.
                        if (sprite.pixelColors[sprite.frame, currentWidthTexture, currentHeightTexture].A > 0)
                            return false;
                    }
                }

                //If the rightmost point is on the right side of the unrotated sprite, we have to check the pixels 
                //in a triangle; if it is on the top of the unrotated sprite, we need to iterate through a cut-off triangle.

                //rightSideMinY is the right side of the triangle when unrotated.
                float rightSideMinY = leftSideMinY + sprite.sourceRectangle.Width * (float)Math.Tan(MathHelper.ToRadians(sprite.GetRotation()));

                //Slope of the line where y = the inputted y-value.
                float slope = rightSideMinY / (float)(sprite.sourceRectangle.Width);

                //Go through all the points contained in this triangle underneath the inputted y-value.
                for (int currentWidthTexture = sprite.sourceRectangle.X;
                        currentWidthTexture <= sprite.sourceRectangle.Right - 1; currentWidthTexture++)
                {
                    for (int currentHeightTexture = leftSideMinY; currentHeightTexture >= sprite.sourceRectangle.Y &&
                    currentHeightTexture >= Convert.ToInt32(leftSideMinY - ((currentWidthTexture - sprite.sourceRectangle.X) * slope));
                    currentHeightTexture--)
                    {
                        //If the Alpha value is greater than 0, we've found a non-transparent pixel below the inputted y-value.
                        if (sprite.pixelColors[sprite.frame, currentWidthTexture, currentHeightTexture].A > 0)
                            return false;
                    }
                }

                return true;  //No pixels with an alpha value greater than 0 are beneath the inputted y-value.
            }
        }


        //Returns true if the last valid bottom-most pixel in the sprite with an alpha value greater than 0 is above (not equal 
        //to) the inputted Y-coordinate.  False is returned if no pixels with an alpha value greater than zero are found below that
        //y-coordinate line.  This method is intended to help determine whether the sprite should be allowed to collide with a 
        //platform tile (a tile that can be moved up through but not down through).
        public static bool LastValidBottomMostPixelIsAboveYCoordinatePP(Sprite sprite, float yCoord)
        {
            float currentRotation = sprite.GetRotation();
            Vector2 currentPosition = new Vector2(sprite.position.X, sprite.position.Y);

            sprite.SetRotation(sprite.lastValidRotation);
            sprite.position = sprite.lastValidPosition;

            bool aboveYCoordinate = BottomMostPixelIsAboveYCoordinatePP(sprite, yCoord);

            sprite.SetRotation(currentRotation);
            sprite.position = currentPosition;
            return aboveYCoordinate;
        }

        #endregion


        //Quickly finds whether the inputted tile is on or off the screen.
        public static bool IsTileOnScreen(int xCoord, int yCoord)
        {
            return (xCoord >= 0 && xCoord < Level.tiles.GetLength(0) &&
                yCoord >= 0 && yCoord < Level.tiles.GetLength(1));
        }


        //Returns tile coordinates (ranging from [0, 0] to (40, 30)), and takes in on-screen coordinates (ranging from [0, 0] to (1280, 720)).
        public static Vector2 FindWhatTileOnScreenCoordsAreIn(Vector2 coordinates)
        {
            return new Vector2((float)Math.Floor((double)coordinates.X / (double)Tile.Width), (float)Math.Floor((double)coordinates.Y / (double)Tile.Height));
        }


        //Global transformation matrix for the player's sprite.
        public static Matrix FindTransformationMatrix(Sprite sprite)
        {
            if (!sprite.transformationMatrix.HasValue)
            {
                sprite.transformationMatrix = Matrix.CreateTranslation(-sprite.rotationOrigin.X, -sprite.rotationOrigin.Y, 0) *  //Origin
                    Matrix.CreateScale(sprite.scale) * //Scale
                    Matrix.CreateRotationZ(MathHelper.ToRadians(sprite.GetRotation())) *  //Rotation
                    Matrix.CreateTranslation(sprite.position.X + sprite.rotationOrigin.X, sprite.position.Y + sprite.rotationOrigin.Y, 0);  //Position
            }

            return sprite.transformationMatrix.Value;
        }
    }
}