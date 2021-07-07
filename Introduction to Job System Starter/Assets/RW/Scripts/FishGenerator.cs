/*
 * Copyright (c) 2020 Razeware LLC
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * Notwithstanding the foregoing, you may not use, copy, modify, merge, publish, 
 * distribute, sublicense, create a derivative work, and/or sell copies of the 
 * Software in any work that is designed, intended, or marketed for pedagogical or 
 * instructional purposes related to programming, coding, application development, 
 * or information technology.  Permission for such use, copying, modification,
 * merger, publication, distribution, sublicensing, creation of derivative works, 
 * or sale is expressly withheld.
 *    
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 */

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;
using UnityEngine.Jobs;

using math = Unity.Mathematics.math;
using random = Unity.Mathematics.Random;


public class FishGenerator : MonoBehaviour
{
    // 1 keep track of each fish's individual velocity throughout the lifetime of the game
    //  in order to simulate continuous movement
    private NativeArray<Vector3> velocities;

    // 2 cant use ref values between threads so use Unity's TransformAccessArray to
    //  take the value type info of a transfor including position, rotation, and matrices. 
    // This also allows you to make realtime transform read/writes I think
    private TransformAccessArray transformAccessArray;

    private PositionUpdateJob positionUpdateJob;

    private JobHandle positionUpdateJobHandle;


    [Header("References")]
    public Transform waterObject;
    public Transform objectPrefab;

    [Header("Spawn Settings")]
    public int amountOfFish;
    public Vector3 spawnBounds;
    public float spawnHeight;
    public int swimChangeFrequency;

    [Header("Settings")]
    public float swimSpeed;
    public float turnSpeed;

    private void Start()
    {
        // 1 initialize velocities with a persistent allocator of size amountOfFish, which is a pre-declared var
        velocities = new NativeArray<Vector3>(amountOfFish, Allocator.Persistent);

        // 2 initialize transformAccessArray with size amountOfFish
        transformAccessArray = new TransformAccessArray(amountOfFish);

        for (int i = 0; i < amountOfFish; i++)
        {

            float distanceX =
            Random.Range(-spawnBounds.x / 2, spawnBounds.x / 2);

            float distanceZ =
            Random.Range(-spawnBounds.z / 2, spawnBounds.z / 2);

            // 3 create a random spawn point withing spawnBounds
            Vector3 spawnPoint =
            (transform.position + Vector3.up * spawnHeight) + new Vector3(distanceX, 0, distanceZ);

            // 4 instantiate objectPrefab (in this case a fish or seal) at spawPoint with no rotation
            Transform t =
            (Transform)Instantiate(objectPrefab, spawnPoint,
            Quaternion.identity);

            // 5 add the instantiated transform to transformAccessArray
            transformAccessArray.Add(t);
        }

    }

    private void Update()
    {
        // 1 convert the data from the main thread into native data types for the job
        positionUpdateJob = new PositionUpdateJob()
        {
            objectVelocities = velocities,
            jobDeltaTime = Time.deltaTime,
            swimSpeed = this.swimSpeed,
            turnSpeed = this.turnSpeed,
            time = Time.time,
            swimChangeFrequency = this.swimChangeFrequency,
            center = waterObject.position,
            bounds = spawnBounds,
            // gets the current milisecond from the system tie to ensure a different seed for each call
            seed = System.DateTimeOffset.Now.Millisecond
        };

        // 2 schedule positionUpdateJob. Note that each job type has it's own Schedule(x, y, z) parameters >(x,y,z)
        positionUpdateJobHandle = positionUpdateJob.Schedule(transformAccessArray);

    }

    //ensures completeion of the job before moving onto the next Update() cycle
    private void LateUpdate()
    {
        positionUpdateJobHandle.Complete();
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireCube(transform.position + Vector3.up * spawnHeight, spawnBounds);
    }

    private void OnDestroy()
    {
        transformAccessArray.Dispose();
        velocities.Dispose();
    }

    [BurstCompile]
    struct PositionUpdateJob : IJobParallelForTransform
    {
        public NativeArray<Vector3> objectVelocities;

        public Vector3 bounds;
        public Vector3 center;

        public float jobDeltaTime;
        public float time;
        public float swimSpeed;
        public float turnSpeed;
        public int swimChangeFrequency;

        public float seed;

        public void Execute(int i, TransformAccess transform)
        {
            // 1 sets the current velocity of the fish
            Vector3 currentVelocity = objectVelocities[i];

            // 2 uses Unity's Mathematics library to creat a psuedorandom number generator that creates a seed by using the index and system time           
            random randomGen = new random((uint)(i * time + 1 + seed));

            // 3 moves the transform along its local forward direction, using localToWorldMatrix
            transform.position +=
            transform.localToWorldMatrix.MultiplyVector(new Vector3(0, 0, 1)) *
            swimSpeed *
            jobDeltaTime *
            randomGen.NextFloat(0.3f, 1.0f);

            // 4 rotates the transform in the directio of the currentVelocity
            if (currentVelocity != Vector3.zero)
            {
                transform.rotation =
                Quaternion.Lerp(transform.rotation,
                Quaternion.LookRotation(currentVelocity), turnSpeed * jobDeltaTime);
            }

            //---------------------KEEPS PREFABS INside THE SPAWN ZONE---------------------------------------------\\

            Vector3 currentPosition = transform.position;

            bool randomise = true;

            // 1 check the position of the transform aagainst the boundaries. If outside, the velocity flips toward the center
            if (currentPosition.x > center.x + bounds.x / 2 ||
                currentPosition.x < center.x - bounds.x / 2 ||
                currentPosition.z > center.z + bounds.z / 2 ||
                currentPosition.z < center.z - bounds.z / 2)
            {
                Vector3 internalPosition = new Vector3(center.x +
                randomGen.NextFloat(-bounds.x / 2, bounds.x / 2) / 1.3f,
                0,
                center.z + randomGen.NextFloat(-bounds.z / 2, bounds.z / 2) / 1.3f);

                currentVelocity = (internalPosition - currentPosition).normalized;

                objectVelocities[i] = currentVelocity;

                transform.rotation = Quaternion.Lerp(transform.rotation,
                Quaternion.LookRotation(currentVelocity),
                turnSpeed * jobDeltaTime * 2);

                randomise = false;
            }

            // 2 if the transform is within the boundaries, there's a small possibility that the direfction of the
            //  shift will give the fish a more natural movement
            if (randomise)
            {
                if (randomGen.NextInt(0, swimChangeFrequency) <= 2)
                {
                    objectVelocities[i] = new Vector3(randomGen.NextFloat(-1f, 1f),
                    0, randomGen.NextFloat(-1f, 1f));
                }
            }


        }
    }


}