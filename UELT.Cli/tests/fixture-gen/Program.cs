using System.Reflection;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

// Builds two tiny StringTable assets from scratch (no game data involved):
//   ST_UeltSmoke.uasset             - versioned UE4.13 package (auto-detected by UAssetAPI/UELT)
//   ST_UeltSmoke_Unversioned.uasset - the same package saved unversioned (needs --engine 4.13)
// The output is deterministic, so re-running it produces byte-identical files.
//
//   dotnet run --project UELT.Cli/tests/fixture-gen -- UELT.Cli/tests/fixtures/uasset

string outDir = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(outDir);

Build(unversioned: false).Write(Path.Combine(outDir, "ST_UeltSmoke.uasset"));
Build(unversioned: true).Write(Path.Combine(outDir, "ST_UeltSmoke_Unversioned.uasset"));

var check = new UAsset(Path.Combine(outDir, "ST_UeltSmoke.uasset"));
Console.WriteLine($"OK: {((StringTableExport)check.Exports[0]).Table.Count} entries, {check.GetEngineVersion()}");
return;

static UAsset Build(bool unversioned)
{
    var a = new UAsset(EngineVersion.VER_UE4_13);

    // Header defaults of a cooked UE4.13 package that a freshly constructed UAsset leaves unset.
    Set(a, "LegacyFileVersion", -6);
    Set(a, "_packageFlags", EPackageFlags.PKG_FilterEditorOnly);
    Set(a, "WillSerializeNameHashes", (bool?)true);
    Set(a, "AssetRegistryRecords", Activator.CreateInstance(TypeOf(a, "AssetRegistryRecords")));
    Set(a, "AdditionalFiles", Array.Empty<byte>());
    Set(a, "ChunkIDs", Array.Empty<int>());
    Set(a, "OverrideNameMapHashes", Activator.CreateInstance(TypeOf(a, "OverrideNameMapHashes")));
    Set(a, "AdditionalPackagesToCook", new List<FString>());
    Set(a, "PayloadTocOffset", -1L);
    Set(a, "DataResourceOffset", -1);
    Set(a, "doWeHaveWorldTileInfo", false);
    Set(a, "PackageSource", 0x55454C54u);

    a.ClearNameIndexList();
    a.Imports = new List<Import>();
    a.Exports = new List<Export>();
    a.SoftPackageReferenceList = new List<FString>();
    a.DependsMap = new List<int[]> { Array.Empty<int>() };
    a.PackageGuid = new Guid("55454c54-0000-0000-0000-000000000001");
    a.FolderName = new FString("None");
    a.IsUnversioned = unversioned;

    FName N(string s)
    {
        a.AddNameReference(new FString(s));
        return new FName(a, s);
    }

    a.AddNameReference(new FString("/Game/UELT/ST_UeltSmoke"));
    a.Imports.Add(new Import(N("/Script/CoreUObject"), N("Package"), new FPackageIndex(0), N("/Script/Engine"), false));
    a.Imports.Add(new Import(N("/Script/CoreUObject"), N("Class"), new FPackageIndex(-1), N("StringTable"), false));
    a.Imports.Add(new Import(N("/Script/Engine"), N("StringTable"), new FPackageIndex(-1), N("Default__StringTable"), false));
    N("None");

    var export = new StringTableExport(new FStringTable(new FString("UELT.Smoke")), a, Array.Empty<byte>())
    {
        ObjectName = N("ST_UeltSmoke"),
        ClassIndex = new FPackageIndex(-2),
        SuperIndex = new FPackageIndex(0),
        TemplateIndex = new FPackageIndex(-3),
        OuterIndex = new FPackageIndex(0),
        ObjectFlags = EObjectFlags.RF_Public | EObjectFlags.RF_Standalone | EObjectFlags.RF_Transactional,
        Data = new List<PropertyData>(),
    };
    export.Table.Add(new FString("Menu.Start"), new FString("Start Game"));
    export.Table.Add(new FString("Menu.Quit"), new FString("Quit"));
    export.Table.Add(new FString("Item.Sword"), new FString("Iron sword"));
    export.Table.Add(new FString("Dialog.Hello"), new FString("Hello, {PlayerName}!"));
    a.Exports.Add(export);

    a.Generations = new List<FGenerationInfo> { new(1, a.GetNameMapIndexList().Count) };
    return a;
}

static MemberInfo Member(object o, string name)
    => o.GetType().GetMember(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)[0];

static Type TypeOf(object o, string name)
    => Member(o, name) is FieldInfo f ? f.FieldType : ((PropertyInfo)Member(o, name)).PropertyType;

static void Set(object o, string name, object value)
{
    switch (Member(o, name))
    {
        case FieldInfo f:
            f.SetValue(o, Convert(value, f.FieldType));
            break;
        case PropertyInfo p when p.CanWrite:
            p.SetValue(o, Convert(value, p.PropertyType));
            break;
        default:
            var backing = o.GetType().GetField($"<{name}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
                          ?? throw new InvalidOperationException("Cannot set " + name);
            backing.SetValue(o, Convert(value, backing.FieldType));
            break;
    }
}

static object Convert(object value, Type t)
    => value == null || t.IsInstanceOfType(value) ? value : System.Convert.ChangeType(value, Nullable.GetUnderlyingType(t) ?? t);
