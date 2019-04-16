using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ComputerVision : MonoBehaviour
{
    public Dictionary<uint, int[,]> circlePoints = null;
    private uint circleSteps = 0;

    private float threshold = 0.60f;


    public void initialiseCirclePoints(uint minRadius, uint maxRadius, uint steps)
    {
        if (minRadius >= maxRadius)
        {
            throw new Exception("min radius should be smaller than max radius");
        }

        circlePoints = new Dictionary<uint, int[,]>();
        circleSteps = steps;

        for (uint radius = minRadius; radius < maxRadius; radius++)
        {
            // generate circle points 3 pixels wide
            int[,] points = new int[steps, 2];
            for (uint step = 0; step < steps; step++)
            {
                points[step, 0] = (int)(radius * Math.Cos(2.0 * Math.PI * step / steps));
                points[step, 1] = (int)(radius * Math.Sin(2.0 * Math.PI * step / steps));
            }

            circlePoints.Add(radius, points);
        }
    }

    // Start is called before the first frame update
    public List<Tuple<uint, int, int>> detectCircles(byte[] cameraImage, int imageWidth, int imageHeight, int locX, int locY, int searchRange, uint minRadius, uint maxRadius)
    {
        Debug.Log("In detect circles");

        if (searchRange <= 0)
        {
            throw new Exception("Invalid search range");
        }

        // return circle location and radius
        List<Tuple<uint, int, int>> circles = new List<Tuple<uint, int, int>>();

        int maxImageIndex = imageWidth * imageHeight;

        for (int y = locY - searchRange; y < locY + searchRange; y++)
        {
            for (int x = locX - searchRange; x < locX + searchRange; x++)
            {
                // (x, y) coordinates of the center of the circle
                // for each radius
                for (uint radius = minRadius; radius < maxRadius; radius++)
                {
                    int[,] pointsToTest;
                    bool validRadius = circlePoints.TryGetValue(radius, out pointsToTest);

                    if (validRadius && testIfCircle(cameraImage, imageWidth, imageHeight, x, y, pointsToTest))
                    {
                        // add to circles found with radius & center x,y
                        circles.Add(new Tuple<uint, int, int>(radius, x, y));
                    }

                }
            }
        }

        return circles;
    }

    private bool testIfCircle(byte[] cameraImage, int imageWidth, int imageHeight, int centerX, int centerY, int[,] points)
    {
        //Debug.Log("In test for circle centerX = " + centerX + " centerY = " + centerY + " radius " + points[0, 0]);

        float numPositivePoints = 0;

        for (int i = 0; i < circleSteps; i++)
        {
            int xcoord = centerX + points[i, 0];
            int ycoord = centerY + points[i, 1];

            // Make sure point is within the image
            if (ycoord >= 0
                    && ycoord < imageHeight 
                    && xcoord >= 0
                    && xcoord < imageWidth)
            {
                if (cameraImage[ycoord * imageWidth + xcoord] == 0xFF)
                {
                    numPositivePoints += 1.0f;
                }
            }
        }

        if (numPositivePoints / circleSteps > threshold)
        {
            Debug.Log("Found circle: centerX = " + centerX + " centerY = " + centerY + " radius " + points[0,0] + " confidence " + numPositivePoints / circleSteps);
            return true;
        }

        return false;
    }
}
