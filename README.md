# Unity-URP-SSR-in-Forward-using-Rendergraph-and-RenderFeature
Screen Space Reflection implemented in Unity URP Forward/Forward+ pipeline, fully adapted to Rendergraph and RenderFeature in Unity 6.

## Motivation
Rendergraph was introduced in Unity 6. I personally think it is a great feature for handling large projects when modifying the rendering pipeline. It is similar to the Render Dependency Graph (RDG) in Unreal Engine. Rendergraph visualizes the rendering pipeline as a graph, and allows developers to inject custom render passes at different stages. It also organizes GPU resources more efficiently and makes graphics programming cleaner.

However, there is very little documentation or examples of using Rendergraph and RenderFeature in Unity URP. To help fill this gap, I implemented SSR in Unity using both Rendergraph and RenderFeature.

## Screen space reflection
There are many SSR tutorials available online. These links were especially helpful during my implementation:

<https://lettier.github.io/3d-game-shaders-for-beginners/screen-space-reflection.html>

<https://github.com/JoshuaLim007/Unity-ScreenSpaceReflections-URP>

<https://github.com/jiaozi158/UnitySSReflectionURP>

<https://www.ea.com/frostbite/news/stochastic-screen-space-reflections>

The basic idea is as follows: you shoot a ray from the pixel, intersect it with the scene geometry, reflect the ray, then perform ray marching from the hit point along the reflected ray direction. At each step, you compare the current ray depth against the depth stored in the depth buffer. If the current depth is greater than the buffer depth, it means the reflected ray has hit a surface. In that case, you fetch the reflected color from the color buffer.

This technique is widely used in game engines because it is straightforward to implement and produces acceptable results. However, it comes with intrinsic limitations:

* If the reflected ray hits an object not visible in the color buffer, the reflection cannot be displayed.

* If the reflected ray passes through the back side of an object without properly intersecting it, visual artifacts may occur.

## To-Do's
There are some issues to fix and improvement to do, including HiZ buffer, blurring, and ray marching in different spaces, which I listed in issues. 

## How to use
Download the repo and open it in Unity. You can find a sample scene in MG_ScreenSpaceReflection folder. You need to add render feature in your own URP renderer(Universal Render Data) and set the Universal Render Pipeline Asset as your current render pipeline. 

The smoothness is controlled by the material's smoothness property if you are using Unity's URP materials(Universal Render Pipeline/Lit, Universal Render Pipeline/LitComplex, ...). Metallic property controls how "reflective" is the material. 

I'm considering a way to incorporate all the materials, not only materials in URP, to my SSR. But currently I think it will only interact with the material that has properties called "_Metallic" or "_Smoothness". 
