using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Runs against the actual Release DLL and installed game classes in Unity, not a
// duplicate implementation. This is component validation, not a Valheim playtest.
public static class EffectsChecks
{
    private static Assembly mod, game, bep;
    private static int checks;
    private static readonly BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    public static void Run()
    {
        string managed = @"G:\Games\SteamLibrary\steamapps\common\Valheim\valheim_Data\Managed";
        string core = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Thunderstore Mod Manager\DataFolder\Valheim\profiles\Default\BepInEx\core");
        AppDomain.CurrentDomain.AssemblyResolve += (sender,args) => {
            string name = new AssemblyName(args.Name).Name + ".dll";
            foreach (string directory in new[] { managed, core }) {
                string path = Path.Combine(directory,name);
                if (File.Exists(path) && !name.StartsWith("UnityEngine")) return Assembly.LoadFrom(path);
            }
            return null;
        };
        Assembly.LoadFrom(Path.Combine(managed,"assembly_utils.dll"));
        game = Assembly.LoadFrom(Path.Combine(managed,"assembly_valheim.dll"));
        bep = Assembly.LoadFrom(Path.Combine(core,"BepInEx.dll"));
        string testRoot=Path.GetFullPath(Path.Combine(Application.dataPath,"..","..","artifacts","test-bepinex"));
        bep.GetType("BepInEx.Paths").GetMethod("SetExecutablePath",All).Invoke(null,new object[]{Path.Combine(testRoot,"valheim.exe"),testRoot,managed,new[]{core,managed}});
        Assembly harmony = Assembly.LoadFrom(Path.Combine(core,"0Harmony.dll"));
        mod = Assembly.LoadFrom(Path.GetFullPath(Path.Combine(Application.dataPath,"..","..","bin","Release","EffectsControl.dll")));
        Check(mod.GetName().Version == new Version(0,0,6,0), "Release assembly version");
        Check(!mod.GetReferencedAssemblies().Any(a => a.Name == "System.ValueTuple"), "No ValueTuple runtime dependency");
        Visuals(); ShieldShape(); Catalog(); Configuration(); MigratedConfiguration();
        // Patch all real installed signatures, then unpatch without executing an attack.
        Type ht = harmony.GetType("HarmonyLib.Harmony",true);
        object patcher = Activator.CreateInstance(ht,new object[]{"Slikfoul.EffectsControl.ComponentChecks"});
        try { ht.GetMethod("PatchAll",new[]{typeof(Assembly)}).Invoke(patcher,new object[]{mod}); Check(true,"Harmony patches bind installed game signatures"); }
        finally { ht.GetMethod("UnpatchSelf").Invoke(patcher,null); }
        string message = "EffectsControl 0.0.6 Unity checks passed: " + checks;
        Debug.Log(message);
        File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"..","..","artifacts","unity-checks.txt")),message + "\nComponent tests in Unity Editor; no Valheim runtime playtest.\n");
    }

    private static void Visuals()
    {
        GameObject root = new GameObject("weapon fixture"); root.transform.localScale = new Vector3(2,3,4);
        root.transform.position = new Vector3(4,5,6);
        var collider = root.AddComponent<SphereCollider>(); collider.radius = 7;
        var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.mass = 13;
        var audio = root.AddComponent<AudioSource>(); audio.volume = 0.7f;
        GameObject ambient = new GameObject("unrelated weather and building light");
        Light unrelated = ambient.AddComponent<Light>(); unrelated.intensity = 9;
        Light light = new GameObject("impact light").AddComponent<Light>(); light.transform.SetParent(root.transform); light.intensity=8; light.range=12;
        GameObject bubble = GameObject.CreatePrimitive(PrimitiveType.Sphere); bubble.name="bubble mesh"; bubble.transform.SetParent(root.transform);
        var filter = bubble.GetComponent<MeshFilter>(); Mesh mesh = filter.sharedMesh;
        var meshCollider = bubble.GetComponent<SphereCollider>(); float meshRadius=meshCollider.radius;
        Material material = new Material(Shader.Find("Standard")); material.color = new Color(0.8f,0.4f,0.2f,0.6f); material.SetColor("_EmissionColor", new Color(4,2,1,1));
        var renderer = bubble.GetComponent<MeshRenderer>(); renderer.sharedMaterial=material;
        Vector3[] originalVertices=mesh.vertices; Vector3 visualScale=bubble.transform.localScale;
        var ps = new GameObject("cloud").AddComponent<ParticleSystem>(); ps.transform.SetParent(root.transform);
        ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=ps.main; main.maxParticles=32; main.startLifetime=60; main.startSize=4; main.simulationSpace=ParticleSystemSimulationSpace.World;
        var emission=ps.emission; emission.enabled=false; emission.rateOverTime=new ParticleSystem.MinMaxCurve(20,40); emission.rateOverDistance=10;
        var burst=new ParticleSystem.Burst(0.2f, new ParticleSystem.MinMaxCurve(12,20)); burst.cycleCount=3; burst.repeatInterval=0.5f; burst.probability=0.8f;
        emission.SetBursts(new[]{burst});
        ps.Play(); ps.Pause();
        var initial=new[]{Particle(7,4,200,30),Particle(7,6,120,20)}; ps.SetParticles(initial,2);
        Check(ps.GetParticles(initial)==2,"Fixture contains two native particles before observation");
        bool fog=RenderSettings.fog; float fogDensity=RenderSettings.fogDensity;
        object state = New("VisualState",root,Stage("Impact"));
        try
        {
            object half=Options(0.5f,0.5f,0.5f,0.5f,0.5f);
            Call(state,"Apply",half);
            Near(light.intensity,4,"Light halved"); Near(light.range,12,"Light range preserved");
            Near(emission.rateOverTime.constantMin,10,"Emission lower bound"); Near(emission.rateOverTime.constantMax,20,"Emission upper bound");
            Near(emission.rateOverDistance.constant,5,"Distance emission");
            var actualBurst=new ParticleSystem.Burst[1]; emission.GetBursts(actualBurst);
            Near(actualBurst[0].count.constantMax,10,"Burst count"); Near(actualBurst[0].time,0.2f,"Burst timing unchanged");
            Check(actualBurst[0].cycleCount==3 && actualBurst[0].repeatInterval==0.5f && actualBurst[0].probability==0.8f,"Burst schedule preserved");
            var values=new ParticleSystem.Particle[2]; Check(ps.GetParticles(values)==2,"Particle count retained");
            Near(values[0].startSize,2,"Particle size halved"); Check(values[0].startColor.a==100 && values[1].startColor.a==60,"Particle alpha and duplicate seeds");
            Check(values[0].position==initial[0].position && values[0].velocity==initial[0].velocity && values[0].remainingLifetime==initial[0].remainingLifetime,"Particle physics and lifetime retained: position="+values[0].position+"/"+initial[0].position+" velocity="+values[0].velocity+"/"+initial[0].velocity+" lifetime="+values[0].remainingLifetime+"/"+initial[0].remainingLifetime);
            values=new[]{values[1],values[0]}; ps.SetParticles(values,2);
            for(int i=0;i<20;i++) Call(state,"Apply",half);
            ps.GetParticles(values); Check(values[0].startColor.a==60 && values[1].startColor.a==100,"Repeated and reordered particles do not compound");
            Near(light.intensity,4,"Repeated light does not compound"); Near(filter.sharedMesh.vertices[0].x,originalVertices[0].x*0.5f,"Mesh shrink from native vertices");
            Check(mesh.vertices.SequenceEqual(originalVertices) && filter.sharedMesh!=mesh,"Shared mesh untouched");
            Check(renderer.sharedMaterial!=material && material.color.a==0.6f,"Shared material untouched");
            Near(renderer.sharedMaterial.color.a,0.3f,"Mesh material alpha"); Near(renderer.sharedMaterial.GetColor("_EmissionColor").r,2,"HDR emission scaled");
            // Simulate a native light fade writing a new intensity before LateUpdate.
            light.intensity=3; Call(state,"Apply",half); Near(light.intensity,1.5f,"Native animated light fade respected");
            Call(state,"Apply",Options(0,0,0,0,0));
            Check(renderer.forceRenderingOff && ps.GetComponent<ParticleSystemRenderer>().forceRenderingOff,"Zero hides renderers"); Near(light.intensity,0,"Zero removes light contribution");
            Near(emission.rateOverTime.constantMax,0,"Zero removes automatic emission");
            Call(state,"Apply",New("VisualOptions")); ps.GetParticles(values);
            Check(!renderer.forceRenderingOff && !ps.GetComponent<ParticleSystemRenderer>().forceRenderingOff,"100 restores visible renderers");
            Check(values[0].startColor.a==120 && values[1].startColor.a==200,"100 restores existing native alpha after zero");
            Near(values[0].startSize,6,"100 restores existing native size after zero"); Near(light.intensity,3,"100 restores latest native fade");
            Check(filter.sharedMesh.vertices.SequenceEqual(originalVertices),"100 restores mesh vertices");
            Near(emission.rateOverTime.constantMax,40,"100 restores density");
            object shaped=New("VisualOptions"); Set(shaped,"Offset",new Vector3(0.25f,0.1f,0)); Set(shaped,"TintEnabled",true); Set(shaped,"Tint",new Color(0.2f,0.1f,0.05f));
            Call(state,"Apply",shaped);
            Vector3 expected=originalVertices[0]+new Vector3(0.25f,0.1f,0);
            Check((filter.sharedMesh.vertices[0]-expected).sqrMagnitude<0.000001f,"Shield offset preserves native dimensions");
            Near(renderer.sharedMaterial.color.r,0.2f,"Shield tint");
            Check(root.transform.localScale==new Vector3(2,3,4) && root.transform.position==new Vector3(4,5,6) && bubble.transform.localScale==visualScale,"Gameplay transforms unchanged");
            Check(collider.radius==7 && meshCollider.radius==meshRadius && body.mass==13 && body.isKinematic && audio.volume==0.7f,"Colliders, rigidbody and sound unchanged");
            Check(unrelated.intensity==9 && RenderSettings.fog==fog && RenderSettings.fogDensity==fogDensity,"Unrelated light and weather unchanged");
        }
        finally { ((IDisposable)state).Dispose(); }
        Check(filter.sharedMesh==mesh && renderer.sharedMaterial==material,"Dispose releases mesh/material ownership");
        Near(emission.rateOverTime.constantMax,40,"Dispose restores emission");
        UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(ambient); UnityEngine.Object.DestroyImmediate(material);
    }

    private static void ShieldShape()
    {
        var root=new GameObject("vfx_StaffShield"); root.transform.localScale=new Vector3(2,3,4);
        var sphere=GameObject.CreatePrimitive(PrimitiveType.Sphere); sphere.name="Sphere"; sphere.transform.SetParent(root.transform); sphere.transform.localScale=Vector3.one*2.5f;
        var other=GameObject.CreatePrimitive(PrimitiveType.Cube); other.name="Other shield mesh"; other.transform.SetParent(root.transform);
        var filter=sphere.GetComponent<MeshFilter>(); Mesh original=filter.sharedMesh;
        Vector3[] vertices=original.vertices, normals=original.normals; Vector4[] tangents=original.tangents;
        Mesh otherMesh=other.GetComponent<MeshFilter>().sharedMesh; Vector3[] otherVertices=otherMesh.vertices;
        float radius=sphere.GetComponent<SphereCollider>().radius;
        object state=New("VisualState",root,Stage("Shield"));
        try {
            object options=New("VisualOptions");
            foreach(float ovality in new[]{0f,0.5f,1f,0.5f,0f}) {
                Set(options,"ShieldOvality",ovality); Call(state,"Apply",options);
                Vector3 extent=filter.sharedMesh.bounds.size, native=original.bounds.size;
                Near(extent.x,native.x*(1-ovality*0.5f),"Shield oval horizontal extent "+ovality);
                Near(extent.y,native.y,"Shield oval retains height "+ovality);
                Near(extent.z,native.z*(1-ovality*0.5f),"Shield oval depth extent "+ovality);
                Check(other.GetComponent<MeshFilter>().sharedMesh.vertices.SequenceEqual(otherVertices),"Other shield mesh retains its shape");
            }
            Check(filter.sharedMesh.normals.SequenceEqual(normals) && filter.sharedMesh.tangents.SequenceEqual(tangents),"Native normals and tangents restored after oval changes");
            Set(options,"ShieldOvality",0.5f); Call(state,"Apply",options);
            Vector3[] ovalVertices=filter.sharedMesh.vertices;
            for(int i=0;i<20;i++) Call(state,"Apply",options);
            Check(filter.sharedMesh.vertices.SequenceEqual(ovalVertices),"Oval shape does not accumulate");
            Check(original.vertices.SequenceEqual(vertices) && original.normals.SequenceEqual(normals),"Original shield mesh remains unchanged");
            Check(root.transform.localScale==new Vector3(2,3,4) && sphere.transform.localScale==Vector3.one*2.5f && sphere.GetComponent<SphereCollider>().radius==radius,"Oval leaves transforms and collider unchanged");
            Call(state,"Apply",New("VisualOptions")); Check(filter.sharedMesh.vertices.SequenceEqual(vertices),"Disabling oval restores native vertices");
        } finally { ((IDisposable)state).Dispose(); }
        Check(filter.sharedMesh==original,"Shield disposal restores original mesh resource");
        object weapon=New("VisualState",root,Stage("Flight"));
        try { object options=New("VisualOptions"); Set(options,"ShieldOvality",1f); Call(weapon,"Apply",options); Check(filter.sharedMesh.vertices.SequenceEqual(vertices),"Oval option never reshapes weapon sphere"); }
        finally { ((IDisposable)weapon).Dispose(); }
        UnityEngine.Object.DestroyImmediate(root);
    }

    private static void Catalog()
    {
        object catalog=New("EffectCatalog");
        Type itemType=game.GetType("ItemDrop+ItemData",true), sharedType=game.GetType("ItemDrop+ItemData+SharedData",true), attackType=game.GetType("Attack",true), listType=game.GetType("EffectList",true);
        object weapon=Activator.CreateInstance(itemType), shared=Activator.CreateInstance(sharedType), attack=Activator.CreateInstance(attackType);
        Set(weapon,"m_shared",shared); Set(shared,"m_itemType",Enum.Parse(game.GetType("ItemDrop+ItemData+ItemType"),"TwoHandedWeapon"));
        Set(shared,"m_attack",attack); Set(attack,"m_attackType",Enum.Parse(game.GetType("Attack+AttackType"),"Area"));
        object impact=ListWithPrefab("fx_sledge_demolisher_hit"), start=ListWithPrefab("fx_iceshard_launch"), ambient=ListWithPrefab("weather fog");
        Set(shared,"m_triggerEffect",impact); Set(attack,"m_burstEffect",start);
        Call(catalog,"RegisterItem",weapon);
        Check(TryList(catalog,impact,"Impact"),"Area trigger classified as impact"); Check(TryList(catalog,start,"Launch"),"Weapon burst classified as launch");
        object[] unknown={ambient,Stage("Launch")}; Check(!(bool)catalog.GetType().GetMethod("TryList").Invoke(catalog,unknown),"Unrelated effect list excluded");
        object tool=Activator.CreateInstance(itemType), toolShared=Activator.CreateInstance(sharedType); Set(tool,"m_shared",toolShared); Set(toolShared,"m_itemType",Enum.Parse(game.GetType("ItemDrop+ItemData+ItemType"),"Tool")); Set(toolShared,"m_triggerEffect",ambient);
        Call(catalog,"RegisterItem",tool); Check(!(bool)catalog.GetType().GetMethod("TryList").Invoke(catalog,unknown),"Tool/building effects excluded");
        GameObject projectile=new GameObject("staff_projectile"); projectile.SetActive(false);
        Component component=projectile.AddComponent(game.GetType("Projectile"));
        object hit=ListWithPrefab("fx_iceshard_hit"); Set(component,"m_hitEffects",hit); Set(attack,"m_attackProjectile",projectile); Call(catalog,"RegisterAttack",attack);
        Check(TryList(catalog,hit,"Impact"),"Projectile hit provenance followed");
        var clone=new GameObject("staff_projectile(Clone)"); object[] lookup={clone,Stage("Launch")}; Check((bool)catalog.GetType().GetMethod("TryProjectile").Invoke(catalog,lookup) && lookup[1].ToString()=="Flight","Network clone exact prefab registration");
        clone.name="staff_projectile_fake(Clone)"; Check(!(bool)catalog.GetType().GetMethod("TryProjectile").Invoke(catalog,lookup),"No heuristic name matching");
        var shield=ScriptableObject.CreateInstance(game.GetType("SE_Shield")); object shieldStart=ListWithPrefab("vfx_StaffShield"); Set(shield,"m_startEffects",shieldStart); Call(catalog,"RegisterShield",shield); Check(TryList(catalog,shieldStart,"Shield"),"Magic shield status effect provenance");
        UnityEngine.Object.DestroyImmediate(shield); UnityEngine.Object.DestroyImmediate(projectile); UnityEngine.Object.DestroyImmediate(clone);
        // Destroy fixture prefabs referenced by each list.
        foreach(object list in new[]{impact,start,ambient,hit,shieldStart}) foreach(object entry in (Array)Get(list,"m_effectPrefabs")) UnityEngine.Object.DestroyImmediate((GameObject)Get(entry,"m_prefab"));
    }

    private static void Configuration()
    {
        string path=Path.GetFullPath(Path.Combine(Application.dataPath,"..","..","artifacts","config-checks.cfg"));
        if(File.Exists(path)) File.Delete(path);
        object config=Activator.CreateInstance(bep.GetType("BepInEx.Configuration.ConfigFile"),new object[]{path,false});
        object settings=Activator.CreateInstance(mod.GetType("EffectsControl.Settings"),All,null,new[]{config},null);
        Check(ConfigValue(settings,"Light")==0 && ConfigValue(settings,"Glow")==50 && ConfigValue(settings,"Opacity")==25 && ConfigValue(settings,"Size")==100 && ConfigValue(settings,"Density")==100,"Requested factory weapon defaults");
        Call(config,"Save"); Check(File.ReadAllText(path).Contains("4. Magic Shield"),"English shield config serializes");
        string configText=File.ReadAllText(path);
        Check(!System.Text.RegularExpressions.Regex.IsMatch(configText,@"[\u0400-\u04FF]"),"All generated sections, labels and descriptions are English");
        foreach(var field in settings.GetType().GetFields(All)) {
            if(!field.FieldType.IsGenericType || field.FieldType.GetGenericTypeDefinition().FullName!="BepInEx.Configuration.ConfigEntry`1") continue;
            Type valueType=field.FieldType.GetGenericArguments()[0];
            Check(valueType==typeof(int) || valueType==typeof(bool) || (field.Name=="ShieldColor" && valueType==typeof(string)),"Numeric settings use integers; color is one text code: "+field.Name);
        }
        Check(!configText.Contains("Ширина =") && !configText.Contains("Высота =") && !configText.Contains("Глубина ="),"Retired shield dimensions absent from generated config");
        Check(mod.GetType("EffectsControl.VisualOptions").GetField("Shape")==null,"Anisotropic geometry control removed from DLL");
        var get=settings.GetType().GetMethod("Get"); object original=get.Invoke(settings,new[]{Stage("Shield"),(object)true});
        Near((float)Get(original,"Opacity"),1,"SubtleShield conflict returns native visuals");
        var shieldOptions=get.Invoke(settings,new[]{Stage("Shield"),(object)false}); Near((float)Get(shieldOptions,"Size"),1,"Shield keeps native dimensions");
        Near((float)Get(shieldOptions,"ShieldOvality"),0.5f,"Shield default oval restores previous proportions");
        Near((float)Get(original,"ShieldOvality"),0,"Shield conflict restores native shape");
        object entry=Get(settings,"Opacity"); entry.GetType().GetProperty("Value").SetValue(entry,50,null);
        var weapon=get.Invoke(settings,new[]{Stage("Impact"),(object)false}); Near((float)Get(weapon,"Opacity"),0.5f,"Live ConfigEntry change read immediately");
        Check(ConfigValue(settings,"Launch")==100 && ConfigValue(settings,"Flight")==0 && ConfigValue(settings,"Impact")==100,"Requested launch/flight/impact defaults");
        string[] parameters={"Light","Glow","Opacity","Size","Density"};
        float[] bases={0f,0.5f,0.5f,1f,1f};
        foreach(string stage in new[]{"Launch","Flight","Impact"}) {
            object stageEntry=Get(settings,stage);
            foreach(int influence in new[]{0,50,100}) {
                stageEntry.GetType().GetProperty("Value").SetValue(stageEntry,influence,null);
                var actual=get.Invoke(settings,new[]{Stage(stage),(object)false});
                for(int i=0;i<parameters.Length;i++) Near((float)Get(actual,parameters[i]),1+(bases[i]-1)*influence/100f,stage+" influence "+influence+" for "+parameters[i]);
            }
        }
        SetConfig(settings,"Launch",0); SetConfig(settings,"Flight",0); SetConfig(settings,"Impact",100);
        foreach(string stage in new[]{"Launch","Flight"}) {
            var actual=get.Invoke(settings,new[]{Stage(stage),(object)false});
            foreach(string parameter in parameters) Near((float)Get(actual,parameter),1,"Launch/flight independent of reductions: "+stage+" "+parameter);
        }
        weapon=get.Invoke(settings,new[]{Stage("Impact"),(object)false});
        for(int i=0;i<parameters.Length;i++) Near((float)Get(weapon,parameters[i]),bases[i],"Impact alone applies globals: "+parameters[i]);
        foreach(string parameter in parameters) SetConfig(settings,parameter,0);
        var nativeFlight=get.Invoke(settings,new[]{Stage("Flight"),(object)false});
        var hiddenImpact=get.Invoke(settings,new[]{Stage("Impact"),(object)false});
        foreach(string parameter in parameters) {
            Near((float)Get(nativeFlight,parameter),1,"Zero globals cannot hide independent flight: "+parameter);
            Near((float)Get(hiddenImpact,parameter),0,"Zero globals hide fully influenced impact: "+parameter);
        }
        for(int i=0;i<parameters.Length;i++) SetConfig(settings,parameters[i],Mathf.RoundToInt(bases[i]*100));
        object offset=Get(settings,"ShieldOffsetX"); offset.GetType().GetProperty("Value").SetValue(offset,25,null);
        object colorEntry=Get(settings,"ShieldColor"); colorEntry.GetType().GetProperty("Value").SetValue(colorEntry,"800000",null);
        shieldOptions=get.Invoke(settings,new[]{Stage("Shield"),(object)false});
        Near(((Vector3)Get(shieldOptions,"Offset")).x,0.25f,"Integer centimeters convert to precise internal meters");
        Near(((Color)Get(shieldOptions,"Tint")).r,128f/255f,"Single color code preserves selected RGB precision");
        Check(!configText.Contains("Красный =") && !configText.Contains("Зелёный =") && !configText.Contains("Синий ="),"Separate RGB sliders absent");
        entry=Get(settings,"Enabled"); entry.GetType().GetProperty("Value").SetValue(entry,false,null);
        var off=get.Invoke(settings,new[]{Stage("Impact"),(object)false}); Near((float)Get(off,"Size"),1,"Disabling restores original coefficients");
        // Exercise the actual observation queue: child initialization must see native intensity.
        GameObject pluginOwner=new GameObject("isolated EffectsControl plugin"); pluginOwner.SetActive(false);
        Component plugin=pluginOwner.AddComponent(mod.GetType("EffectsControl.Plugin"));
        Set(plugin,"ready",true); Set(plugin,"settings",settings);
        entry.GetType().GetProperty("Value").SetValue(entry,true,null);
        SetConfig(settings,"Flight",100);
        SetConfig(settings,"Light",35);
        var effectOwner=new GameObject("initializing projectile"); Light light=effectOwner.AddComponent<Light>(); light.intensity=8;
        Call(plugin,"Observe",effectOwner,Stage("Flight")); Near(light.intensity,8,"Observation does not alter children before Awake/Start completes");
        light.intensity=10; // A native initialization step establishes its final intensity.
        Call(plugin,"LateUpdate"); Near(light.intensity,3.5f,"Deferred capture uses initialized native light");
        Call(plugin,"Observe",effectOwner,Stage("Flight")); Call(plugin,"LateUpdate"); Near(light.intensity,3.5f,"Duplicate observation does not compound");
        GameObject placed=new GameObject("piece returned from EffectList"); placed.SetActive(false); placed.AddComponent(game.GetType("Piece")); Light placedLight=placed.AddComponent<Light>(); placedLight.intensity=7;
        Call(plugin,"Observe",placed,Stage("Impact")); Call(plugin,"LateUpdate"); Near(placedLight.intensity,7,"A Piece returned from a weapon EffectList is excluded");
        Call(plugin,"OnDestroy"); Near(light.intensity,10,"Plugin unload restores native initialized light");
        UnityEngine.Object.DestroyImmediate(placed); UnityEngine.Object.DestroyImmediate(effectOwner); UnityEngine.Object.DestroyImmediate(pluginOwner);
    }

    private static void MigratedConfiguration()
    {
        string path=Path.GetFullPath(Path.Combine(Application.dataPath,"..","..","artifacts","english-migration-fixture.cfg"));
        object config=Activator.CreateInstance(bep.GetType("BepInEx.Configuration.ConfigFile"),new object[]{path,false});
        object settings=Activator.CreateInstance(mod.GetType("EffectsControl.Settings"),All,null,new[]{config},null);
        string[] fields={"Light","Glow","Opacity","Size","Density","Launch","Flight","Impact","ShieldLight","ShieldGlow","ShieldOpacity","ShieldOvality","ShieldOffsetX","ShieldOffsetY","ShieldOffsetZ"};
        int[] values={100,51,35,55,40,35,100,35,35,45,30,50,26,-26,100};
        for(int i=0;i<fields.Length;i++) Check(ConfigValue(settings,fields[i])==values[i],"English settings bind migrated value: "+fields[i]);
        object color=Get(settings,"ShieldColor"); Check((string)color.GetType().GetProperty("Value").GetValue(color,null)=="3380FF","Migrated color binds to English field");
        object tint=Get(settings,"ShieldTint"); Check((bool)tint.GetType().GetProperty("Value").GetValue(tint,null),"Migrated toggle binds to English field");
    }

    private static object ListWithPrefab(string name)
    {
        object list=Activator.CreateInstance(game.GetType("EffectList")); Type effect=game.GetType("EffectList+EffectData"); object data=Activator.CreateInstance(effect); Set(data,"m_prefab",new GameObject(name)); Array array=Array.CreateInstance(effect,1); array.SetValue(data,0); Set(list,"m_effectPrefabs",array); return list;
    }
    private static bool TryList(object catalog,object list,string stage) { object[] args={list,Stage("Launch")}; return (bool)catalog.GetType().GetMethod("TryList").Invoke(catalog,args) && args[1].ToString()==stage; }
    private static ParticleSystem.Particle Particle(uint seed,float size,byte alpha,float remaining) { return new ParticleSystem.Particle { randomSeed=seed,startSize=size,startColor=new Color32(120,80,40,alpha),startLifetime=60,remainingLifetime=remaining,position=new Vector3(size,1,2),velocity=Vector3.up }; }
    private static object Options(float light,float glow,float opacity,float size,float density) { object o=New("VisualOptions"); Set(o,"Light",light); Set(o,"Glow",glow); Set(o,"Opacity",opacity); Set(o,"Size",size); Set(o,"Density",density); return o; }
    private static object Stage(string name) { return Enum.Parse(mod.GetType("EffectsControl.EffectStage"),name); }
    private static object New(string type,params object[] args) { return Activator.CreateInstance(mod.GetType("EffectsControl."+type,true),All,null,args,null); }
    private static void Set(object target,string field,object value) { target.GetType().GetField(field,All).SetValue(target,value); }
    private static object Get(object target,string field) { return target.GetType().GetField(field,All).GetValue(target); }
    private static void SetConfig(object settings,string field,int value) { object entry=Get(settings,field); entry.GetType().GetProperty("Value").SetValue(entry,value,null); }
    private static int ConfigValue(object settings,string field) { object entry=Get(settings,field); return (int)entry.GetType().GetProperty("Value").GetValue(entry,null); }
    private static object Call(object target,string method,params object[] args) { return target.GetType().GetMethod(method,All).Invoke(target,args); }
    private static void Near(float actual,float expected,string message) { Check(Mathf.Abs(actual-expected)<0.0001f,message+" expected="+expected+" actual="+actual); }
    private static void Check(bool result,string message) { if(!result) throw new Exception(message); checks++; }
}
