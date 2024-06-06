using System;
using System.Diagnostics;
using UnityEngine;

public class FluidSimulation : MonoBehaviour
{
    public ComputeShader AdvectShader;
    public ComputeShader SolveShader;
    public ComputeShader DivergeShader;
    public ComputeShader GradientShader;
    public ComputeShader AddDensityShader;
    public ComputeShader AddVelocityShader;
    public ComputeShader CopyShader;

    //It's like temp.. but it is not.
    public RenderTexture AdvectionStorage;
    public RenderTexture SolutionStorage;
    public RenderTexture DivergenceStorage;
    public RenderTexture GradientStorage;
    public RenderTexture ResultingDensity;
    public RenderTexture ResultingVelocity;

    //The quantities that the compute shader will read from, and also, finally, store in.
    public RenderTexture Pressure;
    public RenderTexture PreviousVelocity;
    public RenderTexture Velocity;
    public RenderTexture Density;
    public RenderTexture PreviousDensity;

    //Constants.
    public int N;
    public float TimeStep;
    public int Iterations;
    public float Diffusion;
    public float Viscosity;
                
    public GameObject Quad;
    public Material material;

    void Start()
    {
        InitializeQuantities();
        BindMaterial();
    }

    void Update()
    {
        Pipeline();
    }

    void BindMaterial()
    {
        material = Quad.GetComponent<Renderer>().material;
        material.mainTexture = Density;
    }

    void InitializeQuantities()
    {
        N = 512;
        TimeStep = 1;
        Iterations = 30;
        Diffusion = 0.0001f;
        Viscosity = 0.0001f;

        // Initialize RenderTexture
        Velocity = CreateRenderTexture(N);
        PreviousVelocity = CreateRenderTexture(N);
        Pressure = CreateRenderTexture(N);
        Density = CreateRenderTexture(N);
        PreviousDensity = CreateRenderTexture(N);

        // Initialize Velocity
        InitializeRandomRenderTexture(Velocity);
        InitializeRandomRenderTexture(PreviousVelocity);
        InitializeRandomRenderTexture(Density);
        InitializeRandomRenderTexture(PreviousDensity);
        InitializeRandomRenderTexture(Pressure);

        AdvectionStorage = CreateRenderTexture(N);
        SolutionStorage = CreateRenderTexture(N);
        DivergenceStorage = CreateRenderTexture(N);
        GradientStorage = CreateRenderTexture(N);
        ResultingDensity = CreateRenderTexture(N);
        ResultingVelocity = CreateRenderTexture(N);
    }


    void InitializeRenderTexture(RenderTexture rt, Color color)
    {
        Texture2D t = CreateTexture2D(N);
        for (int y = 0; y < t.height; y++)
        {
            for (int x = 0; x < t.width; x++)
            {
                t.SetPixel(x, y, color);
            }
        }
        t.Apply();
        Graphics.Blit(t, rt);
    }

    void InitializeRandomRenderTexture(RenderTexture rt)
    {
        Texture2D t = CreateTexture2D(N);
        for (int y = 0; y < t.height; y++)
        {
            for (int x = 0; x < t.width; x++)
            {
                float randomValue = UnityEngine.Random.value;
                t.SetPixel(x, y, new Color(randomValue, randomValue, randomValue, 1.0f));
            }
        }
        t.Apply();
        Graphics.Blit(t, rt);
    }

    Texture2D CreateTexture2D(int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBAFloat, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        return texture;
    }

    RenderTexture CreateRenderTexture(int size)
    {
        var rt = new RenderTexture(size, size, 0)
        {
            enableRandomWrite = true,
            format = RenderTextureFormat.ARGBFloat
        };
        rt.Create();
        return rt;
    }

    void InitializeRandomTexture2D(Texture2D texture)
    {
        for (int y = 0; y < texture.height; y++)
        {
            for (int x = 0; x < texture.width; x++)
            {
                texture.SetPixel(x, y, new Color(UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value, 1.0f));
            }
        }
        texture.Apply();
    }

    void InitializeTexture2D(Texture2D texture, Color color)
    {
        for (int y = 0; y < texture.height; y++)
        {
            for (int x = 0; x < texture.width; x++)
            {
                texture.SetPixel(x, y, color);
            }
        }
        texture.Apply();
    }



    void DispatchShader(ComputeShader shader, int kernel)
    {
        int threadGroupsX = Mathf.CeilToInt(N / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(N / 8.0f);
        shader.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);
    }

    RenderTexture Solve(RenderTexture x, RenderTexture x0, float a, float c)
    {
        int kernel = SolveShader.FindKernel("Solve");
        SolveShader.SetTexture(kernel, "Solution", SolutionStorage);
        SolveShader.SetTexture(kernel, "X", x);
        SolveShader.SetTexture(kernel, "X0", x0);
        SolveShader.SetFloat("A", a);
        SolveShader.SetFloat("C", c);
        for (int i = 0; i < Iterations; ++i)
        {
            DispatchShader(SolveShader, kernel);
            Copy(SolutionStorage, x);
        }
        return SolutionStorage;
    }

    RenderTexture Diverge(RenderTexture velocity)
    {
        int kernel = DivergeShader.FindKernel("Diverge");
        DivergeShader.SetTexture(kernel, "Divergence", DivergenceStorage);
        DivergeShader.SetTexture(kernel, "Velocity", velocity);
        DivergeShader.SetInt("N", N);
        DispatchShader(DivergeShader, kernel);
        return DivergenceStorage;
    }

    RenderTexture Gradient(RenderTexture velocity, RenderTexture pressure)
    {
        int kernel = GradientShader.FindKernel("Gradient");
        GradientShader.SetTexture(kernel, "ResultingVelocity", GradientStorage);
        GradientShader.SetTexture(kernel, "Pressure", pressure);
        GradientShader.SetTexture(kernel, "Velocity", velocity);
        GradientShader.SetInt("N", N);
        DispatchShader(GradientShader, kernel);
        return GradientStorage;
    }

    RenderTexture Advect(RenderTexture quantity, RenderTexture toAdvectOverVelocity)
    {
        int kernel = AdvectShader.FindKernel("Advect");
        AdvectShader.SetTexture(kernel, "Quantity", AdvectionStorage);
        AdvectShader.SetTexture(kernel, "ToAdvectQuantity", quantity);
        AdvectShader.SetTexture(kernel, "ToAdvectOverVelocity", toAdvectOverVelocity);
        AdvectShader.SetFloat("TimeStep", TimeStep);
        AdvectShader.SetInt("N", N);
        DispatchShader(AdvectShader, kernel);

        return AdvectionStorage;
    }

    RenderTexture Diffuse(RenderTexture x, RenderTexture x0, float diffusion)
    {
        float a = TimeStep * diffusion * (N - 2) * (N - 2);
        return Solve(x, x0, a, 1 + 6 * a);
    }

    RenderTexture Project(RenderTexture velocity)
    {
        RenderTexture divergence = Diverge(velocity);

        RenderTexture solvedPressure = Solve(Pressure, divergence, 1, 6);

        return Gradient(velocity, solvedPressure);
    }

    public void Copy(RenderTexture source, RenderTexture target)
    {
        int kernel = CopyShader.FindKernel("Copy");
        CopyShader.SetTexture(kernel, "Target", target);
        CopyShader.SetTexture(kernel, "Source", source);
        DispatchShader(CopyShader, kernel);
    }

    public void Pipeline()
    {
        //Diffuse Previous Velocity over The current Velocity (Result is stored in previous velocity)
        RenderTexture diffusedPreviousVelocity = Diffuse(PreviousVelocity, Velocity, Viscosity);
        //Project diffused Previous Velocity (Result is stored in previous velocity).
        RenderTexture correctedPreviousVelocity = Project(diffusedPreviousVelocity);
        Copy(correctedPreviousVelocity, PreviousVelocity);

        //Advect current velocity over previous velocity (result is stored in current velocity)
        RenderTexture advectedCurrentVelocity = Advect(Velocity, PreviousVelocity);
        //Project current advected velocity (Result is stored in current velocity)
        RenderTexture correctedCurrentVelocity = Project(advectedCurrentVelocity);
        Copy(correctedCurrentVelocity, Velocity);

        //Diffuse Previous Density over The current Density (Result is stored in previous Density)
        RenderTexture diffusedPreviousDensity = Diffuse(PreviousDensity, Density, Diffusion);
        Copy(diffusedPreviousDensity, PreviousDensity);

        //Advect current Density over current velocity (result is stored in current density)
        RenderTexture advectedCurrentDensity = Advect(Density, Velocity);
        Copy(advectedCurrentDensity, Density);

    }

    void CopyRenderTextureToTexture2D(RenderTexture rt, Texture2D texture)
    {
        RenderTexture.active = rt;
        texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        texture.Apply();
        RenderTexture.active = null;
    }

    RenderTexture CreateRenderTextureFromTexture2D(Texture2D texture)
    {
        var rt = new RenderTexture(texture.width, texture.height, 0)
        {
            enableRandomWrite = true,
        };
        rt.Create();
        Graphics.Blit(texture, rt);
        return rt;
    }

    void AddDensity(int x, int y, float amount)
    {
        Texture2D D = new Texture2D(N, N);
        CopyRenderTextureToTexture2D(Density, D);
        Color currentDensity = D.GetPixel(x, y);
        float newDensity = currentDensity.r + amount;
        D.SetPixel(x, y, new Color(newDensity, newDensity, newDensity, 1.0f));
        D.Apply();
        Graphics.Blit(D, Density);
        D = null;
    }

    void AddVelocity(int x, int y, float amountX, float amountY)
    {
        Texture2D V = new Texture2D(N, N);
        CopyRenderTextureToTexture2D(Velocity, V);
        Color currentVelocity = V.GetPixel(x, y);
        float newVelocityX = currentVelocity.r + amountX;
        float newVelocityY = currentVelocity.g + amountY;
        V.SetPixel(x, y, new Color(newVelocityX, newVelocityY, 0.0f, 1.0f));
        V.Apply();
        Graphics.Blit(V, Velocity);
        V = null;
    }

    //void AddDensity(int x, int y, float amount)
    //{
    //    int kernel = AddDensityShader.FindKernel("AddDensity");
    //    AddDensityShader.SetTexture(kernel, "ResultingDensity", ResultingDensity);
    //    AddDensityShader.SetTexture(kernel, "Density", Density);
    //    AddDensityShader.SetInt("X", x);
    //    AddDensityShader.SetInt("Y", y);
    //    AddDensityShader.SetFloat("Amount", amount);
    //    AddDensityShader.Dispatch(kernel, 1, 1, 1);
    //    Graphics.Blit(ResultingDensity, Density);
    //}

    //void AddVelocity(int x, int y, float amountX, float amountY)
    //{
    //    int kernel = AddVelocityShader.FindKernel("AddVelocity");
    //    AddVelocityShader.SetTexture(kernel, "ResultingVelocity", ResultingVelocity);
    //    AddVelocityShader.SetTexture(kernel, "Velocity", Velocity);
    //    AddVelocityShader.SetInt("X", x);
    //    AddVelocityShader.SetInt("Y", y);
    //    AddVelocityShader.SetFloat("AmountX", amountX);
    //    AddVelocityShader.SetFloat("AmountY", amountY);
    //    AddVelocityShader.Dispatch(kernel, 1, 1, 1);
    //    Graphics.Blit(ResultingVelocity, Velocity);
    //}
    void HandleMouseInput()
    {

        if (Input.GetMouseButtonDown(0))
        {
            AddVelocity((int)(N / 2), (int)(N / 2), 10.0f, 10.0f);
            AddDensity((int)(N / 2), (int)(N / 2), 10000);
        }
            
    }

}
