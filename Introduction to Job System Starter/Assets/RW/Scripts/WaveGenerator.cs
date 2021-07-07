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
using UnityEngine.Jobs;
using Unity.Collections;
using Unity.Burst;
using Unity.Jobs;
using Unity.Mathematics;

public class WaveGenerator : MonoBehaviour
{
    JobHandle meshModificationJobHandle; // 1 this ensures the main thread waits for the job's completion. can add dependencies that 
                                         //  ensure a job only starts after another job completes. this prevents two jobs from changing the same data at the 
                                         //  same time. It segments the logical flow of you game.
    UpdateMeshJob meshModificationJob; // 2 reference an UpdateMeshJob so the entire class can access it (this WaveGenerator class)


    [Header("Water Mesh Data")]
    NativeArray<Vector3> waterVertices;
    NativeArray<Vector3> waterNormals;


    [Header("Wave Parameters")]
    public float waveScale;
    public float waveOffsetSpeed;
    public float waveHeight;

    [Header("References and Prefabs")]
    public MeshFilter waterMeshFilter;
    private Mesh waterMesh;

    private void Start()
    {
        waterMesh = waterMeshFilter.mesh;

        waterMesh.MarkDynamic(); // 1 mark as dynamic so Unity can optimize sending vertex changes from CPU to GPU

        waterVertices =
        new NativeArray<Vector3>(waterMesh.vertices, Allocator.Persistent); // 2 initialize waterVertices with the vertices of the mesh, you also assign a persistent allocator

        waterNormals =
        new NativeArray<Vector3>(waterMesh.normals, Allocator.Persistent);

    }

    private void Update()
    {
        // 1 initialize the UpdateMeshJob with all the variables required for the job (translate data here, like Time.time to a float?)
        // this is basically the data layer taken and translated from the main thread running at Runtime if I understand right
        // list all of the variable you'll need for the job here:
        meshModificationJob = new UpdateMeshJob()
        {
            vertices = waterVertices,
            normals = waterNormals,
            offsetSpeed = waveOffsetSpeed,
            time = Time.time,
            scale = waveScale,
            height = waveHeight
        };

        // 2  the IJobOarallelFor's Schedule() requires the (*length of the loop*, and the **batch size**) 
        //  the batch size determines how many segments to divide the work into. (How do I determine this?)
        meshModificationJobHandle =
        meshModificationJob.Schedule(waterVertices.Length, 64);

    }
    private void LateUpdate()
    {
        // 1 this ensures the completion of the job becuase you cannot ge the result of the vertices inside the job before it completes
        meshModificationJobHandle.Complete();

        // 2 Unity allows you to directly set the vertices of a mesh from a job. 
        //        This eliminates copying the data back and forth between threads
        waterMesh.SetVertices(meshModificationJob.vertices);

        // 3 need to recalculate the normals of the mesh so that the lighting interacts with the deformed mesh correctly
        waterMesh.RecalculateNormals();

    }


    private void OnDestroy()
    {
        waterVertices.Dispose();
        waterNormals.Dispose();
    }

    [BurstCompile]
    private struct UpdateMeshJob : IJobParallelFor
    {
        // 1 public NativeArray to read and write vertex data between the job and main thread
        public NativeArray<Vector3> vertices;

        // 2 the ReadOnly tag tells the Job System that you only want to read the data from the main thread
        [ReadOnly]
        public NativeArray<Vector3> normals;

        // 3 these control how the noise function acts. The main thread passes them on
        public float offsetSpeed;
        public float scale;
        public float height;

        // 4 Note that you cannot access statics such as Time.time within a job. 
        //  Instead, you pass them in as variables during the job’s initialization.
        public float time;

        private float Noise(float x, float y)
        {
            float2 pos = math.float2(x, y);
            return noise.snoise(pos);
        }


        public void Execute(int i)
        {
            // 1 ensure the wave movement only affects the vertices facing upwards. 
            // This excludes the base of the water effect. 
            if (normals[i].z > 0f)
            {
                // 2 reference the current vertex
                var vertex = vertices[i];

                // 3 sample noise with with scaling and offset transformations
                float noiseValue =
                Noise(vertex.x * scale + offsetSpeed * time, vertex.y * scale +
                offsetSpeed * time);

                // 4 apply the value fo the current vertex within the 'vertices' var
                vertices[i] =
                new Vector3(vertex.x, vertex.y, noiseValue * height + 0.3f);
            }

        }

    }


}