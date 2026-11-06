using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HarfBuzzSharp;
using MsBox.Avalonia;
using MsBox.Avalonia.Base;
using MsBox.Avalonia.Enums;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using MbIcon = MsBox.Avalonia.Enums.Icon;

namespace x2_prototype;

public partial class MainWindow : Window
{
    string? inputExePath;
    string? outputExePath;
    string? inputFolder;
    string? outputFolder;
    List<uint> relocationEntries = new();
    uint? newPersiansPowerAddressA = null; // lazy fix

    public ObservableCollection<object> Entries { get; } = new()
    {
        new CbpItem("CBP Alpha 11"),
        new ReassignmentItem("Normal", "AMERICANS = 0x14", "Persians despotism (A)"),
        new ReassignmentItem("Normal", "AMERICANS = 0x14", "Persians despotism (B)"),
        new ReassignmentItem("Reverse", "NO_NATION = 0x69", "Bantu pop cap / final pop cap"),
        new ReassignmentItem("Normal", "LAKOTA = 0x13", "Chinese herbal lore"),
        //new AsmBlockItem("Get Z tech automatically and for free", "LAKOTA = 0x13", null, null, "HERBAL_LORE=603", null),
        new AsmBlockItem("X unit is Y% cheaper to build", "TURKS = 0x08", "CATAPULT=265", 69, null, null),
        new AsmBlockItem("X unit is Y% faster to build", "GREEKS = 0x05", "SCHOLARS=52", 42, null, null),
        new AsmBlockItem("Start game with Z1 tech", "BRITISH = 0x0B", null, null, "GOV_MONARCHY=625", null),
        new AsmBlockItem("Start game with Z2 building", "EGYPTIANS = 0x07", null, null, null, "PYRAMIDS=526"),
    };

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        AsmBlockTypeCombo.SelectionChanged += AsmBlockTypeCombo_SelectionChanged;   // fixes UpdateAsmBlockInputs() from firing before everything's ready
        UpdateAsmBlockInputs();
    }

    private void AddReassignment_Click(object? sender, RoutedEventArgs e)
    {
        if (AssignmentTypeCombo.SelectedItem is not ComboBoxItem typeItem
            || AssignmentNationCombo.SelectedItem is not ComboBoxItem colorItem
            || NationPowerCombo.SelectedItem is not ComboBoxItem powerItem)
        {
            return;
        }

        Entries.Add(new ReassignmentItem(
            typeItem.Content?.ToString() ?? "",
            colorItem.Content?.ToString() ?? "",
            powerItem.Content?.ToString() ?? ""));
    }

    private void AddAsmBlock_Click(object? sender, RoutedEventArgs e)
    {
        if (AsmBlockTypeCombo.SelectedItem is not ComboBoxItem typeItem
            || AsmBlockNationCombo.SelectedItem is not ComboBoxItem nationItem
            || AsmBlockUnitCombo.SelectedItem is not ComboBoxItem unitItem
            || AsmBlockValueInput.Value is not decimal value
            || AsmBlockTechCombo.SelectedItem is not ComboBoxItem techItem
            || AsmBlockBuildingCombo.SelectedItem is not ComboBoxItem buildingItem)
        {
            return;
        }

        Entries.Add(new AsmBlockItem(
            typeItem.Content?.ToString() ?? "",
            nationItem.Content?.ToString() ?? "",
            unitItem.Content?.ToString() ?? "",
            (uint)value,
            techItem.Content?.ToString() ?? "",
            buildingItem.Content?.ToString() ?? ""));
    }

    private void RemoveEntry_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: object entry })
        {
            Entries.Remove(entry);
        }
    }

    // Adapted from mechcommander mantis project to use Avalonia stuff
    private async void BrowseInput_Click(object? sender, RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            return;
        }

        var startFolder = inputFolder ?? AppDomain.CurrentDomain.BaseDirectory;
        var startLocation = await storage.TryGetFolderFromPathAsync(startFolder);

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select compatible Rise of Nations exe (e.g., riseofnations_CBPa11.exe)",
            AllowMultiple = false,
            SuggestedStartLocation = startLocation,
            SuggestedFileName = "riseofnations_CBPa11.exe",
            FileTypeFilter =
            [
                new FilePickerFileType("Executable files") { Patterns = ["*.exe"] },
                new FilePickerFileType("All files") { Patterns = ["*"] }
            ]
        });

        var selectedPath = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrEmpty(selectedPath))
        {
            return;
        }

        inputExePath = selectedPath;
        InputExeTextBox.Text = inputExePath;

        var folder = Path.GetDirectoryName(inputExePath);
        if (folder is null)
        {
            return;
        }

        inputFolder = folder;

        if (outputExePath is null)
        {
            outputExePath = Path.Combine(folder, "riseofnations_X2.exe");
            OutputExeTextBox.Text = outputExePath;
            outputFolder = folder;
        }
    }

    // Adapted from mechcommander mantis project to use Avalonia stuff
    private async void BrowseOutput_Click(object? sender, RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            return;
        }

        outputFolder ??= inputFolder;
        var startFolder = outputFolder ?? AppDomain.CurrentDomain.BaseDirectory;
        var startLocation = await storage.TryGetFolderFromPathAsync(startFolder);

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Select name and location for saving patched exe (e.g., riseofnations_X2.exe)",
            SuggestedStartLocation = startLocation,
            SuggestedFileName = "riseofnations_X2.exe",
            DefaultExtension = "exe",
            FileTypeChoices =
            [
                new FilePickerFileType("Executable files") { Patterns = ["*.exe"] },
                new FilePickerFileType("All files") { Patterns = ["*"] }
            ]
        });

        var selectedPath = file?.TryGetLocalPath();
        if (string.IsNullOrEmpty(selectedPath))
        {
            return;
        }

        if (!selectedPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            selectedPath += ".exe";

        outputExePath = selectedPath;
        OutputExeTextBox.Text = outputExePath;
        outputFolder = Path.GetDirectoryName(outputExePath);
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        relocationEntries.Clear();

        try
        {
            await ApplyChangesAndSaveFile();

            string title = "File saved";
            string msg = $"Changes applied and saved successfully to:\n{outputExePath}";
            var msgbox = MessageBoxManager.GetMessageBoxStandard(title, msg, ButtonEnum.Ok, MbIcon.Success);
            await msgbox.ShowAsync();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            string title = "Error: bad address lookup";
            string msg = $"MHLoppy has made a mistake implementing one of the nation power address lookups: {ex}";
            var msgbox = MessageBoxManager.GetMessageBoxStandard(title, msg, ButtonEnum.Ok, MbIcon.Warning);
            await msgbox.ShowAsync();
        }
        catch (ArgumentNullException)
        {
            string title = "Error: No RoN exe";
            string msg = "A compatible RoN game exe is required - use the first \"Browse...\" button in the bottom-right of the program.";
            var msgbox = MessageBoxManager.GetMessageBoxStandard(title, msg, ButtonEnum.Ok, MbIcon.Warning);
            await msgbox.ShowAsync();
        }
        catch (ArgumentException ex)
        {
            string title = "Incompatible exe";
            string msg = $"The supplied RoN game exe is not a known variation of CBP Alpha 11 - patching aborted.";
            var msgbox = MessageBoxManager.GetMessageBoxStandard(title, msg, ButtonEnum.Ok, MbIcon.Error);
            await msgbox.ShowAsync();
        }
        catch (FileNotFoundException ex)
        {
            string title = $"Error: File not found";
            string msg = $"{ex}";
            var msgbox = MessageBoxManager.GetMessageBoxStandard(title, msg, ButtonEnum.Ok, MbIcon.Warning);
            await msgbox.ShowAsync();
        }
        catch (InvalidDataException ex)
        {
            string title = "Error: Invalid data";
            string msg = $"{ex}";
            var msgbox = MessageBoxManager.GetMessageBoxStandard(title, msg, ButtonEnum.Ok);
            if (await msgbox.ShowAsync() == ButtonResult.No)
            {
                throw new OperationCanceledException("Patching aborted by user due to existing file.");
            }
        }
        catch (InvalidOperationException)
        {
            string title = "Changes list is empty";
            string msg = "There are no changes on the list, so no action has been taken. Add some changes and try again.";
            var msgbox = MessageBoxManager.GetMessageBoxStandard(title, msg, ButtonEnum.Ok, MbIcon.Info);
            await msgbox.ShowAsync();
        }
        catch (OperationCanceledException)
        {
            string title = "Cancelled";
            string msg = "No action has been taken.";
            var msgbox = MessageBoxManager.GetMessageBoxStandard(title, msg, ButtonEnum.Ok, MbIcon.Info);
            await msgbox.ShowAsync();
        }
        catch (Exception ex)
        {
            string title = "Unhandled error";
            string msg = $"{ex}";
            var msgbox = MessageBoxManager.GetMessageBoxStandard(title, msg, ButtonEnum.Ok, MbIcon.Error);
            await msgbox.ShowAsync();
        }
    }

    // Repurposed from XAML action because it was firing (and causing problems) while XAML was still loading (?); now only lives in code-behind despite its naming scheme
    private void AsmBlockTypeCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateAsmBlockInputs();
    }

    private static bool FileHashMatches(string filePath, string expectedHash)
    {
        string thisHash;

        // https://makolyte.com/csharp-get-a-files-checksum-using-any-hashing-algorithm-md5-sha256/
        using (SHA256 sha256 = SHA256.Create())
        {
            using (var stream = File.OpenRead(filePath))
            {
                var hash = sha256.ComputeHash(stream);
                thisHash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant(); // hash is separated by dashes + capitalized by default
            }
        }

        return (thisHash == expectedHash);
    }

    private async Task ApplyChangesAndSaveFile()
    {
        if (Entries.Count <= 1)
        {
            throw new InvalidOperationException();
        }

        if (inputExePath is null)
        {
            throw new ArgumentNullException();
        }

        if (outputExePath is null)
        {
            throw new Exception(); // shouldn't be possible, so use fall through to a harsher exception
        }

        if (!File.Exists(inputExePath))
        {
            throw new FileNotFoundException($"Game exe not found at: {inputExePath}", nameof(inputExePath));
        }

        if (File.Exists(outputExePath))
        {
            string title = "File of that name already exists";
            string msg = "A file already exists at the specified output path. Do you want to overwrite it?";
            var msgbox = MessageBoxManager.GetMessageBoxStandard(title, msg, ButtonEnum.YesNo);
            if (await msgbox.ShowAsync() == ButtonResult.No)
            {
                throw new OperationCanceledException("Patching aborted by user due to existing file.");
            }
        }

        var hashes = new List<string>
        {
            "fa3b91dd6130876d36e5ff43f07155b7f674c0e77ce9dfee5d43790b074284b5", // CBPa11 non-LAA SHA256
            "2cdc5e86b90312d40c14adb69c9dd813a6f87c172bfd20f7f0195f6ab9e4477b", // CBPa11 LAA flag only (e.g., manual edit or "Large Address Aware" utility)
            "160eaeae8a34b716cad604403b18b489610aed5b4a8795ecca4f311fee183f97"  // CBPa11 LAA flag + checksum (e.g., "4GB patch utility)
        };

        bool hashMatches = false;
        foreach (string hash in hashes)
        {
            if (FileHashMatches(inputExePath, hash))
            {
                hashMatches = true;
            }
        }
        if (!hashMatches)
        {
            throw new ArgumentException();
        }

        // Note: byte arrays in C# are reference types, so this array 
        //   will be updated directly by each of the functions as they run.
        byte[] fileData = File.ReadAllBytes(inputExePath);

        // order swapped so that reassignment can be special-cased to handle persian despotism interception
        ApplyAsmBlocks(fileData);
        ApplyReassignments(fileData);

        File.WriteAllBytes(outputExePath, fileData);

        // To save adapting the code, add relocation entries *after* saving the file already
        if (!UpdateRelocationTable())
        {
            throw new Exception("Error while updating relocation table.");
        }
    }

    private void ApplyReassignments(byte[] fileData)
    {
        var addresses = new Dictionary<string, (uint Address, uint JumpAddress)>
        {
            ["Bantu city cost"]                     = (0x265062, 0x26506A), // 74 JZ
            ["Bantu city limit (A)"]                = (0x2C728F, 0x2C7299), // 74 JZ
            ["Bantu city limit (B)"]                = (0x2D554D, 0x2D5555), // 74 JZ
            ["Bantu pop cap / final pop cap"]       = (0x2DB9D5, 0x2DB9DF), // 74 JZ
            ["Bantu unit upgrades"]                 = (0x2D64E8, 0x2D64F0), // 74 JZ
            ["Bantu units move"]                    = (0x204C1D, 0x204C2B), // 74 JZ
            ["Chinese citizen creation / instant"]  = (0x2500EC, 0x2500FA), // 74 JZ
            ["Chinese herbal lore"]                 = (0x997324, 0x2DE191), // 84 JZ
            ["Chinese knowledge upgrades"]          = (0x2DEDDB, 0x2DEDE6), // 84 JZ
            ["Chinese large city / cities (A)"]     = (0x1AAE43, 0x1AAE51), // 74 JZ
            ["Chinese large city / cities (B)"]     = (0x22328E, 0x2232AC), // 74 JZ
            ["Chinese large city / cities (C)"]     = (0x338313, 0x338321), // 74 JZ
            ["Chinese large city / cities (D)"]     = (0x39567C, 0x395689), // 74 JZ
            ["Chinese science discount"]            = (0x2660C6, 0x997997), // 74 JZ
            ["Chinese light infantry [CBP]"]        = (0x99797A, 0x997982), // 74 JZ
            ["Persians bonus food (A)"]             = (0x38B681, 0x38B683), // 0x85 JNZ (but it's an in-line CMP using 0x17 (!))
            ["Persians bonus food (B)"]             = (0x2E4430, 0x2E443A), // 74 JZ
            ["Persians capitals (A)"]               = (0x223BB0, 0x223BBE), // 74 JZ
            ["Persians capitals (B)"]               = (0x22354C, 0x223CCB), // 74 JZ
            ["Persians capitals (C)"]               = (0x223CB3, 0x223DD9), // 74 JZ
            ["Persians capitals (D)"]               = (0x223DC1, 0x223555), // 84 JZ
            ["Persians capitals (E)"]               = (0x2284D8, 0x2284E4), // 74 JZ
            ["Persians capitals (F)"]               = (0x5FF7BC, 0x5FF7C4), // 74 JZ
            ["Persians capitals (G)"]               = (0x2DBBD1, 0x2DBBDB), // 74 JZ
            ["Persians capitals (H)"]               = (0x3362B5, 0x3362C3), // 74 JZ
            ["Persians capitals (I)"]               = (0x3328AD, 0x3328E9), // 74 JZ
            ["Persians caravans"]                   = (0x21E9DD, 0x21E9EB), // 74 JZ
            ["Persians civic discount"]             = (0x26604F, 0x266058), // 84 JZ
            ["Persians despotism (A)"]              = (0x2E3BE8, 0x2E3BF2), // 74 JZ
            ["Persians despotism (B)"]              = (0x38BDC4, 0x38BDCE), // 74 JZ
            ["Persians taxation"]                   = (0x2DE84F, 0x2DE85A), // 84 JZ
            ["Persian capitals worldgov fix [CBP]"] = (0x997AB0, 0x997AB9), // 84 JZ
            ["Persian cheaper conv missiles [CBP]"] = (0x997B11, 0x997B19), // 74 JZ
            ["Persian faster conv missiles [CBP]"]  = (0x997B6B, 0x997B79), // 74 JZ
        };

        foreach (ReassignmentItem entry in Entries.OfType<ReassignmentItem>())
        {
            // Special case persian despotism power reassignment, because adding starting tech mangles it
            if (entry.AssignmentPower == "Persians despotism (A)" && newPersiansPowerAddressA != null)
            {
                uint offset = (uint)newPersiansPowerAddressA;
                string nationEnumString = entry.AssignmentNation;

                if (byte.TryParse(
                        nationEnumString.AsSpan(nationEnumString.Length - 2),
                        System.Globalization.NumberStyles.HexNumber,
                        null,
                        out byte replacementByte))
                {
                    PatchByteAtOffset(fileData, offset, replacementByte);
                }
                else
                {
                    throw new Exception();
                }

                if (entry.AssignmentType == "Reverse")
                {
                    uint jumpOffset = 0x2E3BF2;
                    FlipJumpAtOffset(fileData, jumpOffset);
                }
            }
            else if (addresses.TryGetValue(entry.AssignmentPower, out var result))
            {
                uint offset = result.Address;
                string nationEnumString = entry.AssignmentNation;

                if (byte.TryParse(
                        nationEnumString.AsSpan(nationEnumString.Length - 2),
                        System.Globalization.NumberStyles.HexNumber,
                        null,
                        out byte replacementByte))
                {
                    PatchByteAtOffset(fileData, offset, replacementByte);
                }
                else
                {
                    throw new Exception();
                }

                if (entry.AssignmentType == "Reverse")
                {
                    uint jumpOffset = result.JumpAddress;
                    FlipJumpAtOffset(fileData, jumpOffset);
                }
            }
            else
            {
                throw new ArgumentOutOfRangeException($"No address mapping found for AssignmentPower '{entry.AssignmentPower}'.", nameof(entry.AssignmentPower));
            }
        }
    }

    private void ApplyAsmBlocks(byte[] fileData)
    {
        int unitCheaperBlocks   = Entries.OfType<AsmBlockItem>().Count(item => item.AsmBlockType == "X unit is Y% cheaper to build");
        int unitFasterBlocks    = Entries.OfType<AsmBlockItem>().Count(item => item.AsmBlockType == "X unit is Y% faster to build");
        //int techFreeBlocks    = Entries.OfType<AsmBlockItem>().Count(item => item.AsmBlockType == "Get Z tech automatically and for free");
        int unitStartBlocks     = Entries.OfType<AsmBlockItem>().Count(item => item.AsmBlockType == "Start game with X unit");
        int techStartBlocks     = Entries.OfType<AsmBlockItem>().Count(item => item.AsmBlockType == "Start game with Z1 tech");
        int buildingStartBlocks = Entries.OfType<AsmBlockItem>().Count(item => item.AsmBlockType == "Start game with Z2 building");

        //foreach (AsmBlockItem entry in Entries.OfType<AsmBlockItem>())
        //{
        //    // the fancy modern C# switch expression which I can't use in .NET Framework lol
        //    currentOffset += entry.AsmBlockType switch
        //    {
        //        "X unit is Y% cheaper to build"         => AddUnitCheaper(fileData, currentOffset),
        //        "X unit is Y% faster to build"          => AddUnitFaster(fileData, currentOffset),
        //        "Get Z tech automatically and for free" => AddTechFree(fileData, currentOffset),
        //        "Start game with Z tech"                => AddTechStart(fileData, currentOffset),
        //        _ => 0
        //    };
        //}

        uint diskOffset = 0x997FF0;
        uint ghidraOffset = 0xfd4df0;

        int unitCheaperCounter = 1;
        bool firstUnitCheaper = true;
        bool lastUnitCheaper = false;

        int unitFasterCounter = 1;
        bool firstUnitFaster = true;
        bool lastUnitFaster = false;

        int unitStartCounter = 1;
        bool firstUnitStart = true;
        bool lastUnitStart = false;

        int techStartCounter = 1;
        bool firstTechStart = true;
        bool lastTechStart = false;

        int buildingStartCounter = 1;
        bool firstBuildingStart = true;
        bool lastBuildingStart = false;

        foreach (var (entry, index) in Entries.OfType<AsmBlockItem>()
            .Where(entry => entry.AsmBlockType == "X unit is Y% cheaper to build")
            .Select((entry, index) => (entry, index)))
        {
            string nationRaw = entry.AsmNation[^2..];
            uint nationEnum = Convert.ToUInt32(nationRaw, 16);
            uint unitX = uint.Parse(entry.AsmUnit.Split('=')[1]);
            uint discountY = entry.AsmValue ?? 0u;

            lastUnitCheaper = (unitCheaperCounter == unitCheaperBlocks);

            uint length = AddUnitCheaper(fileData, diskOffset, ghidraOffset, nationEnum, unitX, discountY, ref relocationEntries, firstUnitCheaper, lastUnitCheaper);
            diskOffset += length;
            ghidraOffset += length;

            firstUnitCheaper = false;
            unitCheaperCounter++;
        }

        foreach (var (entry, index) in Entries.OfType<AsmBlockItem>()
            .Where(entry => entry.AsmBlockType == "X unit is Y% faster to build")
            .Select((entry, index) => (entry, index)))
        {
            string nationRaw = entry.AsmNation[^2..];
            uint nationEnum = Convert.ToUInt32(nationRaw, 16);
            uint unitX = uint.Parse(entry.AsmUnit.Split('=')[1]);
            uint fasterY = entry.AsmValue ?? 0u;

            lastUnitFaster = (unitFasterCounter == unitFasterBlocks);

            uint length = AddUnitFaster(fileData, diskOffset, ghidraOffset, nationEnum, unitX, fasterY, ref relocationEntries, firstUnitFaster, lastUnitFaster);
            diskOffset += length;
            ghidraOffset += length;

            firstUnitFaster = false;
            unitFasterCounter++;
        }

        foreach (var (entry, index) in Entries.OfType<AsmBlockItem>()
            .Where(entry => entry.AsmBlockType == "Start game with X unit")
            .Select((entry, index) => (entry, index)))
        {
            string nationRaw = entry.AsmNation[^2..];
            uint nationEnum = Convert.ToUInt32(nationRaw, 16);
            uint unitX = uint.Parse(entry.AsmUnit.Split('=')[1]);

            lastUnitStart = (unitStartCounter == unitStartBlocks);

            uint length = AddUnitStart(fileData, diskOffset, ghidraOffset, nationEnum, unitX, ref relocationEntries, firstUnitStart, lastUnitStart);
            diskOffset += length;
            ghidraOffset += length;

            firstUnitStart = false;
            unitStartCounter++;
        }

        foreach (var (entry, index) in Entries.OfType<AsmBlockItem>()
            .Where(entry => entry.AsmBlockType == "Start game with Z1 tech")
            .Select((entry, index) => (entry, index)))
        {
            string nationRaw = entry.AsmNation[^2..];
            uint nationEnum = Convert.ToUInt32(nationRaw, 16);
            uint techZ = uint.Parse(entry.AsmTech.Split('=')[1]);

            lastTechStart = (techStartCounter == techStartBlocks);

            uint length = AddTechStart(fileData, diskOffset, ghidraOffset, nationEnum, techZ, ref relocationEntries, firstTechStart, lastTechStart, ref newPersiansPowerAddressA);
            diskOffset += length;
            ghidraOffset += length;

            firstTechStart = false;
            techStartCounter++;
        }

        foreach (var (entry, index) in Entries.OfType<AsmBlockItem>()
            .Where(entry => entry.AsmBlockType == "Start game with Z2 building")
            .Select((entry, index) => (entry, index)))
        {
            string nationRaw = entry.AsmNation[^2..];
            uint nationEnum = Convert.ToUInt32(nationRaw, 16);
            uint buildingZ = uint.Parse(entry.AsmBuilding.Split('=')[1]);

            lastBuildingStart = (buildingStartCounter == buildingStartBlocks);

            uint length = AddBuildingStart(fileData, diskOffset, ghidraOffset, nationEnum, buildingZ, ref relocationEntries, firstBuildingStart, lastBuildingStart);
            diskOffset += length;
            ghidraOffset += length;

            firstBuildingStart = false;
            buildingStartCounter++;
        }
    }

    // based on CBP's Persian bonus for conv missiles
    private static uint AddUnitCheaper(byte[] fileData, uint diskOffset, uint ghidraOffset, uint nationEnum, uint unitX, uint discountY, ref List<uint> relocationEntries, bool firstEntry, bool lastEntry)
    {
        // 0/10: redirect the existing exe's jump to our new blocks
        if (firstEntry)
        {
            byte[] existingJmp = [0xE9, 0xA3, 0x01, 0x00, 0x00];
            byte[] newJmp = [0xE9];
            uint originalJmpGhidra = 0xfd4958;
            uint originalJmpDisk = 0x997B58;
            byte[] newJmpOperand = GetCallOrNearJumpOperand((originalJmpGhidra + 5), ghidraOffset);
            newJmp = newJmp.Concat(newJmpOperand).ToArray();
            PatchBytesAtOffset(fileData, originalJmpDisk, existingJmp, newJmp);
        }

        // 1/10: push the nation enum
        byte[] oneOfTen = [0x6a, (byte)nationEnum];

        // 2/10: call has_tribe_bonus()
        byte[] twoOfTen = [0xe8];//, 0x59, 0xca, 0x70, 0xff];
        uint callOffset = ghidraOffset + 7; // +2 bytes for oneOfTen, +5 bytes for twoOfTen
        // we want to get to ghidra 0x006e1370 (which is 0x2E0770) so we have to calculate the relative address for the CALL operand
        byte[] callOperand = GetCallOrNearJumpOperand(callOffset, 0x6e1370);
        twoOfTen = twoOfTen.Concat(callOperand).ToArray();

        // 3/10: mundane logic
        byte[] threeOfTen = [0x85, 0xc0, 0x74, 0x34];

        // 4/10: block that requires relocation entry
        byte[] fourOfTen = [0xa1, 0xf4, 0xaa, 0xc0, 0x00, 0x6a, 0x00]; // second byte needs relocation entry
        relocationEntries.Add(ghidraOffset + 12); // length of parts 1-3 + 1 to get the second byte here

        // 5/10: unit number
        byte[] fiveOfTen = [0x68];//, 0x39, 0x01, 0x00, 0x00]; // last four bytes are the unit number
        byte[] unitEnum = ToPaddedBytes(unitX, 4);
        fiveOfTen = fiveOfTen.Concat(unitEnum).ToArray();

        // 6/10: mundane logic
        byte[] sixOfTen = [0x8b, 0x0c, 0xb8, 0x8b, 0x01, 0xff, 0x50, 0x60, 0x85
                           , 0xc0, 0x74, 0x1c, 0xb9, 0x64, 0x00, 0x00, 0x00];

        // 7/10: discount number
        byte[] sevenOfTen = [0x83, 0xe9, (byte)discountY]; // last byte is discount number (XAML-limited to max 100; 8e E9 supports up to 7F (127) otherwise have to do 6-byte 81 E9 XX XX XX XX)

        // 8/10: mundane logic
        byte[] eightOfTen = [0xb8, 0x1f, 0x85, 0xeb, 0x51, 0x0f, 0xaf, 0xcb
                             , 0xf7, 0xe9, 0xc1, 0xfa, 0x05, 0x89, 0xd3, 0xc1
                             , 0xeb, 0x1f, 0x03, 0xda, 0x8b, 0x4d, 0x14];

        // 9/10A: needs another relocation entry
        byte[] nineOfTen = [0x03, 0x0d, 0xe0, 0x61, 0xc0, 0x00]; // third byte needs relocation entry

        // 10/10: concluding jump to ghidra 0xfd4b00 / disk 0x997D00
        byte[] tenOfTen = [];//, 0xa3, 0x01, 0x00, 0x00];
        // merge the prior pieces to get the total length so we can calculate the jump address
        byte[] combined = new byte[][]
        {
            oneOfTen,
            twoOfTen,
            threeOfTen,
            fourOfTen,
            fiveOfTen,
            sixOfTen,
            sevenOfTen,
            eightOfTen,
        }
        .SelectMany(array => array) // flatten to array instead of array of arrays
        .ToArray();

        // 9/10B: slip in the relocation entry here because we've merged the bytes and need that for length calculations
        relocationEntries.Add(ghidraOffset + (uint)combined.Length + 2);
        combined = combined.Concat(nineOfTen).ToArray();

        if (lastEntry)
        {
            // final jump
            tenOfTen = [0xE9];
            byte[] jmpOperand = GetCallOrNearJumpOperand((ghidraOffset + (uint)combined.Length + 5), 0xFD4B00);
            tenOfTen = tenOfTen.Concat(jmpOperand).ToArray();
        }

        // padding bytes
        int jmpBytes = 5;
        int paddingCount = (16 - ((combined.Length + jmpBytes) % 16)) % 16;  // the last modulo handles the otherwise-final value being 16 (since 16 - (16%16=0) = 16)
        byte[] padding = new byte[paddingCount];
        Array.Fill(padding, (byte)0xCC);

        byte[] replacementBytes = combined.Concat(padding).ToArray();
        byte[] expectedBytes = new byte[replacementBytes.Length + jmpBytes];
        Array.Fill(expectedBytes, (byte)0x00);

        // Fix the concluding jump when this isn't the last block of this type
        //   We have to do this after the padding etc is done otherwise the relative jump distance will be wrong
        if (!lastEntry)
        {
            tenOfTen = [0xE9];
            byte[] jmpOperand = GetCallOrNearJumpOperand((ghidraOffset + (uint)combined.Length), ghidraOffset + (uint)replacementBytes.Length);
            tenOfTen = tenOfTen.Concat(jmpOperand).ToArray();
        }

        combined = combined.Concat(tenOfTen).ToArray();
        replacementBytes = combined.Concat(padding).ToArray();

        PatchBytesAtOffset(fileData, diskOffset, expectedBytes, replacementBytes);

        return (uint)replacementBytes.Length;
    }

    // based on CBP's Persian bonus for conv missiles
    private static uint AddUnitFaster(byte[] fileData, uint diskOffset, uint ghidraOffset, uint nationEnum, uint unitX, uint fasterY, ref List<uint> relocationEntries, bool firstEntry, bool lastEntry)
    {
        // 0/12: redirect the existing exe's jump to our new blocks
        if (firstEntry)
        {
            byte[] existingJmp = [0xE9, 0x3B, 0xC5, 0x67, 0xFF];
            byte[] newJmp = [0xE9];
            uint originalJmpGhidra = 0xfd49b0;
            uint originalJmpDisk = 0x997BB0;
            byte[] newJmpOperand = GetCallOrNearJumpOperand((originalJmpGhidra + 5), ghidraOffset);
            newJmp = newJmp.Concat(newJmpOperand).ToArray();
            PatchBytesAtOffset(fileData, originalJmpDisk, existingJmp, newJmp);
        }

        // 1/12 mundane logic
        byte[] oneOfTwelve = [0x0f, 0xb6, 0x43, 0x09, 0x69, 0xc8, 0xec, 0x6e, 0x00, 0x00];

        // 2/12 push the nation enum
        byte[] twoOfTwelve = [0x6a, (byte)nationEnum];

        // 3/12 needs relocation entry
        byte[] threeOfTwelve = [0x03, 0x0d, 0xe0, 0x61, 0xc0, 0x00];    // third byte needs relocation entry
        relocationEntries.Add(ghidraOffset + 14);   // length of parts 1-2 + 2 to get the third byte here

        // 4/12 call has_tribe_bonus()
        byte[] fourOfTwelve = [0xe8];//, 0xf9, 0xc9, 0x70, 0xff];
        uint callOffset = ghidraOffset + 23;    // +18 bytes for parts 1-3, + 5 bytes for part 4
        // we want to get to ghidra 0x006e1370 (which is 0x2E0770) so we have to calculate the relative address for the CALL operand
        byte[] callOperand = GetCallOrNearJumpOperand(callOffset, 0x6e1370);
        fourOfTwelve = fourOfTwelve.Concat(callOperand).ToArray();

        // 5/12 mundane logic
        byte[] fiveOfTwelve = [0x85, 0xc0, 0x74, 0x2b];

        // 6/12 needs relocation entry
        byte[] sixOfTwelve = [0xa1, 0x74, 0xa2, 0xc0, 0x00];   // second byte needs relocation entry
        relocationEntries.Add(ghidraOffset + 28); // length of parts 1-5 + 1 to get the second byte here

        // 7/12 mundane logic
        byte[] sevenOfTwelve = [0x6a, 0x00];

        // 8/12 after first byte is unit number (X)
        byte[] eightOfTwelve = [0x68];//, 0x39, 0x01, 0x00, 0x00];
        byte[] unitEnum = ToPaddedBytes(unitX, 4);
        eightOfTwelve = eightOfTwelve.Concat(unitEnum).ToArray();

        // 9/12 mundane logic
        byte[] nineOfTwelve = [0x8b, 0x0c, 0xb0, 0x8b, 0x01, 0xff, 0x50, 0x60
                                , 0x85, 0xc0, 0x74, 0x13];

        // 10/12 after first byte is faster value (Y)
        byte[] tenOfTwelve = [0xb9, (byte)fasterY, 0x00, 0x00, 0x00];

        // 11/12 mundane logic
        byte[] elevenOfTwelve = [0x6b, 0xc7, 0x64, 0x83, 0xc1, 0x64, 0x99, 0xf7
                                , 0xf9, 0x8b, 0xf8, 0x89, 0x7d, 0xfc, 0x0f, 0xb6
                                , 0x43, 0x09, 0x69, 0xc8, 0xec, 0x6e, 0x00, 0x00];

        // 12/12 final jump
        byte[] twelveOfTwelve = [0xe9, 0x3b, 0xc5, 0x67, 0xff];
        // merge the prior pieces to get the total length so we can calculate the jump address
        byte[] combined = new byte[][]
        {
            oneOfTwelve,
            twoOfTwelve,
            threeOfTwelve,
            fourOfTwelve,
            fiveOfTwelve,
            sixOfTwelve,
            sevenOfTwelve,
            eightOfTwelve,
            nineOfTwelve,
            tenOfTwelve,
            elevenOfTwelve
        }
        .SelectMany(array => array) // flatten to array instead of array of arrays
        .ToArray();

        if (lastEntry)
        {
            // final jump
            twelveOfTwelve = [0xE9];
            byte[] jmpOperand = GetCallOrNearJumpOperand((ghidraOffset + (uint)combined.Length + 5), 0x650ef0);
            twelveOfTwelve = twelveOfTwelve.Concat(jmpOperand).ToArray();
        }

        // padding bytes
        int jmpBytes = 5;
        int paddingCount = (16 - ((combined.Length + jmpBytes) % 16)) % 16;  // the last modulo handles the otherwise-final value being 16 (since 16 - (16%16=0) = 16)
        byte[] padding = new byte[paddingCount];
        Array.Fill(padding, (byte)0xCC);

        byte[] replacementBytes = combined.Concat(padding).ToArray();
        byte[] expectedBytes = new byte[replacementBytes.Length + jmpBytes];
        Array.Fill(expectedBytes, (byte)0x00);

        // Fix the concluding jump when this isn't the last block of this type
        //   We have to do this after the padding etc is done otherwise the relative jump distance will be wrong
        if (!lastEntry)
        {
            twelveOfTwelve = [0xE9];
            byte[] jmpOperand = GetCallOrNearJumpOperand((ghidraOffset + (uint)combined.Length), ghidraOffset + (uint)replacementBytes.Length);
            twelveOfTwelve = twelveOfTwelve.Concat(jmpOperand).ToArray();
        }

        combined = combined.Concat(twelveOfTwelve).ToArray();
        replacementBytes = combined.Concat(padding).ToArray();

        PatchBytesAtOffset(fileData, diskOffset, expectedBytes, replacementBytes);

        return (uint)replacementBytes.Length;
    }

    // based on free Dutch merchant
    private static uint AddUnitStart(byte[] fileData, uint diskOffset, uint ghidraOffset, uint nationEnum, uint unitX, ref List<uint> relocationEntries, bool firstEntry, bool lastEntry)
    {
        // 0/10: redirect the existing exe's jump to our new blocks
        //   a bit more complicated this time because it's in-line code rather than broken-out code
        //   in this case we hijack the short jump into the nopcode area, and then put a longer jump in there
        if (firstEntry)
        {
            //byte[] existingJmp1 = [0x74, 0x36];
            //byte[] newJmp1 = [0x74];
            //uint originalJmpGhidra1 = 0x5ab1b0;
            //uint originalJmpDisk1 = 0x1AA5B0;
            //uint newJmpDest1 = 0x5ab1cd;
            //byte[] newJmpOperand1 = GetCallOrNearJumpOperand((originalJmpGhidra1 + 2), newJmpDest1);
            //newJmp1 = newJmp1.Concat(newJmpOperand1).ToArray();
            //PatchBytesAtOffset(fileData, originalJmpDisk1, existingJmp1, newJmp1);

            PatchByteAtOffset(fileData, 0x1AA5B1, 0x1B); // much easier than above - just change the short jump's operand directly

            byte[] existingJmp2 = [0x90, 0x90, 0x90, 0x90, 0x90];
            byte[] newJmp2 = [0xE9];
            uint originalJmpGhidra2 = 0x5ab1cd;
            uint originalJmpDisk2 = 0x1AA5CD;
            byte[] newJmpOperand2 = GetCallOrNearJumpOperand((originalJmpGhidra2 + 5), ghidraOffset);
            newJmp2 = newJmp2.Concat(newJmpOperand2).ToArray();
            PatchBytesAtOffset(fileData, originalJmpDisk2, existingJmp2, newJmp2);
        }

        // 1/10: mundane code
        byte[] oneOfTen = [0x8b, 0x4d, 0x0c];

        // 2/10: push the nation enum
        byte[] twoOfTen = [0x6a, (byte)nationEnum];

        // 3/10: call has_tribe_bonus
        byte[] threeOfTen = [0xe8];//, 0xc2, 0x61, 0x13, 0x00];
        uint callOffset = ghidraOffset + 10;    // +5 bytes for parts 1-2, +5 bytes for part 3
        // we want to get to ghidra 0x006e1370 (which is 0x2E0770) so we have to calculate the relative address for the CALL operand
        byte[] callOperand = GetCallOrNearJumpOperand(callOffset, 0x6e1370);
        threeOfTen = threeOfTen.Concat(callOperand).ToArray();

        // 4/10: mundane code
        byte[] fourOfTen = [0x85, 0xc0];

        // 5/10: failure JZ (retargeted to closing JMP)
        byte[] fiveOfTen = [0x74, 0x1E];    // jumps to our final JMP

        // 6/10: mundane code
        byte[] sixOfTen = [0x51, 0xff, 0x75, 0xf8, 0x8b, 0x4d, 0x0c, 0xff, 0x75, 0xf4];

        // 7/10: after first byte is unit number (original is a 2-byte instruction)
        //byte[] sevenOfTen = [0x6a, 0x3e];
        byte[] sevenOfTen = [0x68];
        byte[] unitEnum = ToPaddedBytes(unitX, 4);
        sevenOfTen = sevenOfTen.Concat(unitEnum).ToArray();

        // 8/10: call to LeaderData::current_upgrade
        byte[] eightOfTen = [0xe8];//, 0x7d, 0x7f, 0x13, 0x00];
        callOffset = ghidraOffset + 34; // + 29 bytes for parts 1-7 (includes expansion of part 7 from a 2-byte to a 5-byte instruction), +5 bytes for part 8
        // we want to get to ghidra 0x006e3140 (which is 0x2E2540) so we have to calculate the relative address for the CALL operand
        callOperand = GetCallOrNearJumpOperand(callOffset, 0x6e3140);
        eightOfTen = eightOfTen.Concat(callOperand).ToArray();

        // 9/10: mundane code
        byte[] nineOfTen = [0x50, 0xff, 0x75, 0x10, 0x53];

        // 10/10: call to Setup::place_unit
        byte[] tenOfTen = [0xe8];//, 0xd3, 0x0a, 0x00, 0x00];
        callOffset = ghidraOffset + 44; // + 39 bytes for parts 1-9 (includes expansion of part 7 from a 2-byte to a 5-byte instruction), +5 bytes for part 10
        // we want to get to ghidra 0x005abca0 (which is 0x1AB0A0) so we have to calculate the relative address for the CALL operand
        callOperand = GetCallOrNearJumpOperand(callOffset, 0x5abca0);
        tenOfTen = tenOfTen.Concat(callOperand).ToArray();

        // the original code doesn't include a closing JMP because it's in-line rather than broken out
        byte[] combined = new byte[][]
        {
            oneOfTen,
            twoOfTen,
            threeOfTen,
            fourOfTen,
            fiveOfTen,
            sixOfTen,
            sevenOfTen,
            eightOfTen,
            nineOfTen,
            tenOfTen
        }
        .SelectMany(array => array) // flatten to array instead of array of arrays
        .ToArray();

        // final jump
        byte[] finalJump = [];
        if (lastEntry)
        {
            finalJump = [0xE9];
            byte[] jmpOperand = GetCallOrNearJumpOperand((ghidraOffset + (uint)combined.Length + 5), 0x005ab1e8);
            finalJump = finalJump.Concat(jmpOperand).ToArray();
        }

        // padding bytes
        int jmpBytes = 5;
        int paddingCount = (16 - ((combined.Length + jmpBytes) % 16)) % 16;  // the last modulo handles the otherwise-final value being 16 (since 16 - (16%16=0) = 16)
        byte[] padding = new byte[paddingCount];
        Array.Fill(padding, (byte)0xCC);

        byte[] replacementBytes = combined.Concat(padding).ToArray();
        byte[] expectedBytes = new byte[replacementBytes.Length + jmpBytes];
        Array.Fill(expectedBytes, (byte)0x00);

        // Fix the concluding jump when this isn't the last block of this type
        //   We have to do this after the padding etc is done otherwise the relative jump distance will be wrong
        if (!lastEntry)
        {
            finalJump = [0xE9];
            byte[] jmpOperand = GetCallOrNearJumpOperand((ghidraOffset + (uint)combined.Length), ghidraOffset + (uint)replacementBytes.Length);
            finalJump = finalJump.Concat(jmpOperand).ToArray();
        }

        combined = combined.Concat(finalJump).ToArray();
        replacementBytes = combined.Concat(padding).ToArray();

        PatchBytesAtOffset(fileData, diskOffset, expectedBytes, replacementBytes);

        return (uint)replacementBytes.Length;
    }

    // based on unused Persians free Despotism
    private static uint AddTechStart(byte[] fileData, uint diskOffset, uint ghidraOffset, uint nationEnum, uint techZ, ref List<uint> relocationEntries, bool firstEntry, bool lastEntry, ref uint? newPersiansPowerAddressA)
    {
        // 0/10: redirect the existing exe's jump to our new blocks
        //   a bit more complicated this time because it's in-line code rather than broken-out code
        //   in this case we mangle the first 3 instructions to make a jump, then unmangle them in the new area
        if (firstEntry)
        {
            // this first one is replacing more than just a jump, which also makes the unmangling more complicated
            //   I've moved it to the end to make the offset tracking easier
            byte[] existingJmp1 = [0x6A, 0x17, 0x8b, 0xcb, 0xe8, 0x80, 0xcb, 0xff, 0xff];
            byte[] newJmp1 = [0x90, 0x90, 0x90, 0x90, 0xE9];
            uint originalJmpGhidra1 = 0x6e47e7;
            uint originalJmpDisk1 = 0x2E3BE7;
            byte[] newJmpOperand1 = GetCallOrNearJumpOperand((originalJmpGhidra1 + 9), ghidraOffset);
            newJmp1 = newJmp1.Concat(newJmpOperand1).ToArray();
            PatchBytesAtOffset(fileData, originalJmpDisk1, existingJmp1, newJmp1);
        }

        // 1/12: push the nation enum
        byte[] oneOfTwelve = [0x6A, (byte)nationEnum];

        // 2/12: mundane logic
        byte[] twoOfTwelve = [0x8B, 0xCB];

        // 3/12: call to has_tribe_bonus
        byte[] threeOfTwelve = [0xe8];//, 0x80, 0xcb, 0xff, 0xff];
        uint callOffset = ghidraOffset + 9; // 4 bytes for parts 1-2, +5 bytes for part 3
        // we want to get to ghidra 0x006e1370 (which is 0x2E0770) so we have to calculate the relative address for the CALL operand
        byte[] callOperand = GetCallOrNearJumpOperand(callOffset, 0x6e1370);
        threeOfTwelve = threeOfTwelve.Concat(callOperand).ToArray();

        // 4/12: mundane logic
        byte[] fourOfTwelve = [0x85, 0xC0];

        // 5/12: this is a JZ for the tribe bonus, but we should be able to leave it alone
        byte[] fiveOfTwelve = [0x74, 0x28];

        //// 6/12 needs relocation
        //byte[] sixOfTwelve = [0xA1, 0xF0, 0x61, 0xC0, 0x00];    // second byte needs relocation entry
        //relocationEntries.Add(ghidraOffset + 14); // length of parts 1-5 + 1 to get the second byte here

        // 6/12 can also be NOP'd out - part of part 8's rules.xml value check
        byte[] sixOfTwelve = [0x90, 0x90, 0x90, 0x90, 0x90];

        // 7/12: mundane logic
        byte[] sevenOfTwelve = [0x33, 0xC9];

        // 8/12: NOP'd out - originally checking for rules.xml value
        byte[] eightOfTwelve = [0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90];

        // 9/12: mundane logic
        byte[] nineOfTwelve = [0x6a, 0x01, 0x51, 0x51, 0x8b, 0xc4, 0x51, 0x89, 0x08, 0x8b, 0xc4];

        // 10/12: after first byte is tech number
        byte[] tenOfTwelve = [0x68];//, 0x6f, 0x02, 0x00, 0x00];
        byte[] techEnum = ToPaddedBytes(techZ, 4);
        tenOfTwelve = tenOfTwelve.Concat(techEnum).ToArray();

        // 11/12: mundane logic
        byte[] elevenOfTwelve = [0x89, 0x08, 0x8b, 0xcb];

        // 12/12: call to gain_tech
        byte[] twelveOfTwelve = [0xE8];//, 0x44, 0x83, 0xFF, 0xFF];
        callOffset = ghidraOffset + 53; // 48 bytes for parts 1-11, +5 bytes for part 12
        // we want to get to ghidra 0x6dcb60 (which is 0x2DBF60) so we have to calculate the relative address for the CALL operand
        callOperand = GetCallOrNearJumpOperand(callOffset, 0x6dcb60);
        twelveOfTwelve = twelveOfTwelve.Concat(callOperand).ToArray();

        // the original code doesn't include a closing JMP because it's in-line rather than broken out
        byte[] combined = new byte[][]
        {
            oneOfTwelve,
            twoOfTwelve,
            threeOfTwelve,
            fourOfTwelve,
            fiveOfTwelve,
            sixOfTwelve,
            sevenOfTwelve,
            eightOfTwelve,
            nineOfTwelve,
            tenOfTwelve,
            elevenOfTwelve,
            twelveOfTwelve
        }
        .SelectMany(array => array) // flatten to array instead of array of arrays
        .ToArray();

        // Unmangle the stuff our initial jump steamrolled over
        if (firstEntry)
        {
            // special-case the persian despotism A assignment if we mangle the original address
            newPersiansPowerAddressA = (diskOffset + (uint)combined.Length + 1);

            byte[] existingBytes = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
            byte[] unmangledBytes = [0x6A, 0x17, 0x8B, 0xCB, 0xE8];//, 0x80, 0xcb, 0xff, 0xff];
            uint unmangledOffset = ghidraOffset + (uint)combined.Length + 9;
            byte[] unmangledOperand = GetCallOrNearJumpOperand(unmangledOffset, 0x6e1370);
            unmangledBytes = unmangledBytes.Concat(unmangledOperand).ToArray();

            combined = combined.Concat(unmangledBytes).ToArray();
        }

        // final jump
        byte[] finalJump = [];
        if (lastEntry)
        {
            finalJump = [0xE9];
            byte[] jmpOperand = GetCallOrNearJumpOperand((ghidraOffset + (uint)combined.Length + 5), 0x006e481c);
            finalJump = finalJump.Concat(jmpOperand).ToArray();
        }

        // padding bytes
        int jmpBytes = 5;
        int paddingCount = (16 - ((combined.Length + jmpBytes) % 16)) % 16;  // the last modulo handles the otherwise-final value being 16 (since 16 - (16%16=0) = 16)
        byte[] padding = new byte[paddingCount];
        Array.Fill(padding, (byte)0xCC);

        byte[] replacementBytes = combined.Concat(padding).ToArray();
        byte[] expectedBytes = new byte[replacementBytes.Length + jmpBytes];
        Array.Fill(expectedBytes, (byte)0x00);

        // Fix the concluding jump when this isn't the last block of this type
        //   We have to do this after the padding etc is done otherwise the relative jump distance will be wrong
        if (!lastEntry)
        {
            finalJump = [0xE9];
            byte[] jmpOperand = GetCallOrNearJumpOperand((ghidraOffset + (uint)combined.Length), ghidraOffset + (uint)replacementBytes.Length);
            finalJump = finalJump.Concat(jmpOperand).ToArray();
        }

        combined = combined.Concat(finalJump).ToArray();
        replacementBytes = combined.Concat(padding).ToArray();

        PatchBytesAtOffset(fileData, diskOffset, expectedBytes, replacementBytes);

        return (uint)replacementBytes.Length;
    }

    // based on free egyptian granary (but all the free buildings use the same code structure)
    private static uint AddBuildingStart(byte[] fileData, uint diskOffset, uint ghidraOffset, uint nationEnum, uint buildingZ, ref List<uint> relocationEntries, bool firstEntry, bool lastEntry)
    {
        // 0/10: redirect the existing exe's jump to our new blocks
        //   this is very similar to the AddTechStart implementation
        //   we mangle the first 3 instructions to make a jump, then unmangle them in the new area (at the end of this func)
        if (firstEntry)
        {
            // this first one is replacing more than just a jump, which also makes the unmangling more complicated
            byte[] existingJmp1 = [0x6a, 0x07, 0x8b, 0xce, 0xe8, 0x29, 0x5b, 0x13, 0x00];
            byte[] newJmp1 = [0x90, 0x90, 0x90, 0x90, 0xE9];
            uint originalJmpGhidra1 = 0x5ab83e;
            uint originalJmpDisk1 = 0x1AAC3E;
            byte[] newJmpOperand1 = GetCallOrNearJumpOperand((originalJmpGhidra1 + 9), ghidraOffset);
            newJmp1 = newJmp1.Concat(newJmpOperand1).ToArray();
            PatchBytesAtOffset(fileData, originalJmpDisk1, existingJmp1, newJmp1);
        }

        // 1/10: push the nation enum
        byte[] oneOfTen = [0x6a, (byte)nationEnum];

        // 2/10: mundane logic
        byte[] twoOfTen = [0x8b, 0xce];

        // 3/10: call has_tribe_bonus
        byte[] threeOfTen = [0xe8];//, 0x29, 0x5b, 0x13, 0x00];
        uint callOffset = ghidraOffset + 9; // 4 bytes for parts 1-2, +5 bytes for part 3
        // we want to get to ghidra 0x006e1370 (which is 0x2E0770) so we have to calculate the relative address for the CALL operand
        byte[] callOperand = GetCallOrNearJumpOperand(callOffset, 0x6e1370);
        threeOfTen = threeOfTen.Concat(callOperand).ToArray();

        // 4/10: mundane logic
        byte[] fourOfTen = [0x85, 0xc0, 0x74, 0x34];

        // 5 / 10: NOP'd out section - originally used for rules.xml lookup
        byte[] fiveOfTen = [0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90
                            , 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90];

        // 6/10: mundane logic
        byte[] sixOfTen = [0x8d, 0x04, 0xfd, 0x00, 0x00, 0x00, 0x00, 0x8b
                           , 0xce, 0x2b, 0xc7, 0x6a, 0x00];

        // 7/10: needs relocation entry
        byte[] sevenOfTen = [0x8b, 0x04, 0x85, 0x70, 0x99, 0xc0, 0x00]; // fourth byte needs relocation entry
        relocationEntries.Add(ghidraOffset + 43);   // length of parts 1-6 + 3 to get the third byte here

        // 8/10: mundane logic
        byte[] eightOfTen = [0x8b, 0x04, 0x98, 0x0f, 0xbf, 0x40, 0x08, 0x50];

        // 9/10: after first byte is building number
        byte[] nineOfTen = [0x68];//, 0xa7, 0x01, 0x00, 0x00];
        byte[] buildingEnum = ToPaddedBytes(buildingZ, 4);
        nineOfTen = nineOfTen.Concat(buildingEnum).ToArray();

        // 10/10: call produce_building
        byte[] tenOfTen = [0xe8];//, 0x81, 0x5b, 0x13, 0x00];
        callOffset = ghidraOffset + 65; // 60 bytes for parts 1-9, +5 bytes for part 10
        // we want to get to ghidra 0x6e1400 (which is 0x2E0800) so we have to calculate the relative address for the CALL operand
        callOperand = GetCallOrNearJumpOperand(callOffset, 0x6e1400);
        tenOfTen = tenOfTen.Concat(callOperand).ToArray();

        // the original code doesn't include a closing JMP because it's in-line rather than broken out
        byte[] combined = new byte[][]
        {
            oneOfTen,
            twoOfTen,
            threeOfTen,
            fourOfTen,
            fiveOfTen,
            sixOfTen,
            sevenOfTen,
            eightOfTen,
            nineOfTen,
            tenOfTen
        }
        .SelectMany(array => array) // flatten to array instead of array of arrays
        .ToArray();

        // Unmangle the stuff our initial jump steamrolled over
        if (firstEntry)
        {
            byte[] existingBytes = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
            byte[] unmangledBytes = [0x6a, 0x07, 0x8b, 0xce, 0xe8];//, 0x29, 0x5B, 0x13, 0x00];
            uint unmangledOffset = ghidraOffset + (uint)combined.Length + 9;
            byte[] unmangledOperand = GetCallOrNearJumpOperand(unmangledOffset, 0x6e1370);
            unmangledBytes = unmangledBytes.Concat(unmangledOperand).ToArray();

            combined = combined.Concat(unmangledBytes).ToArray();
        }

        // final jump
        byte[] finalJump = [];
        if (lastEntry)
        {
            finalJump = [0xE9];
            byte[] jmpOperand = GetCallOrNearJumpOperand((ghidraOffset + (uint)combined.Length + 5), 0x5ab847);
            finalJump = finalJump.Concat(jmpOperand).ToArray();
        }

        // padding bytes
        int jmpBytes = 5;
        int paddingCount = (16 - ((combined.Length + jmpBytes) % 16)) % 16;  // the last modulo handles the otherwise-final value being 16 (since 16 - (16%16=0) = 16)
        byte[] padding = new byte[paddingCount];
        Array.Fill(padding, (byte)0xCC);

        byte[] replacementBytes = combined.Concat(padding).ToArray();
        byte[] expectedBytes = new byte[replacementBytes.Length + jmpBytes];
        Array.Fill(expectedBytes, (byte)0x00);

        // Fix the concluding jump when this isn't the last block of this type
        //   We have to do this after the padding etc is done otherwise the relative jump distance will be wrong
        if (!lastEntry)
        {
            finalJump = [0xE9];
            byte[] jmpOperand = GetCallOrNearJumpOperand((ghidraOffset + (uint)combined.Length), ghidraOffset + (uint)replacementBytes.Length);
            finalJump = finalJump.Concat(jmpOperand).ToArray();
        }

        combined = combined.Concat(finalJump).ToArray();
        replacementBytes = combined.Concat(padding).ToArray();

        PatchBytesAtOffset(fileData, diskOffset, expectedBytes, replacementBytes);

        return (uint)replacementBytes.Length;
    }

    private static void PatchByteAtOffset(byte[] fileData, uint offset, byte replacementByte)
    {
        fileData[offset] = replacementByte;
    }

    private static void PatchBytesAtOffset(byte[] fileData, uint offset, byte[] expectedBytes, byte[] replacementBytes)
    {
        if (replacementBytes.Length != expectedBytes.Length)
        {
            throw new InvalidDataException($"Length of replacement bytes ({replacementBytes.Length}) doesn't match expected length ({expectedBytes.Length}).");
        }

        if (offset + expectedBytes.Length > fileData.Length)
        {
            throw new InvalidDataException($"Offset address {offset:X2} is larger than the file ({fileData.Length} bytes).");
        }

        byte[] bytesAtOffset = new byte[expectedBytes.Length];
        Array.Copy(fileData, offset, bytesAtOffset, 0, expectedBytes.Length);

        if (bytesAtOffset.SequenceEqual(replacementBytes))
        {
            return;
        }

        if (bytesAtOffset.SequenceEqual(expectedBytes))
        {
            Array.Copy(replacementBytes, 0, fileData, offset, replacementBytes.Length);
        }
        else
        {
            // unlike in Mantis I hash-match the RoN exe before patching it, so this shouldn't be hit unless something goes wrong
            throw new InvalidDataException($"Bytes at offset {offset:X2} don't match the expected sequence in the RoN exe.");
        }
    }

    private static void FlipJumpAtOffset(byte[] fileData, uint offset)
    {
        if (fileData[offset] == 0x74)
        {
            fileData[offset] = 0x75;
        }
        else if (fileData[offset] == 0x84)
        {
            fileData[offset] = 0x85;
        }
        else if (fileData[offset] == 0x85)
        {
            fileData[offset] = 0x84;
        }
        else
        {
            // Every byte is explicitly known to be one of the three values above, so this should never be hit
            throw new Exception();
        }
    }

    private static byte[] ToPaddedBytes(uint value, int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxLength);

        byte[] result = new byte[maxLength];    // default byte value is 0; don't need to manually process
        byte[] valueBytes = BitConverter.GetBytes(value);

        Array.Copy(valueBytes, result, Math.Min(maxLength, valueBytes.Length)); // auto-does little-endian on Windows, freeing us from needing to do it manually
        return result;
    }

    private static byte[] GetCallOrNearJumpOperand(uint preCalculatedCallFrom, uint target) // near jumps are the 5 byte jumps starting E9 XX XX XX XX
    {
        // pre-calculated as in it's already factoring in the length of the call itself
        uint displacement = unchecked(target - preCalculatedCallFrom);

        return BitConverter.GetBytes(displacement);
    }

    private bool UpdateRelocationTable()
    {
        Relocations rel = new Relocations(outputExePath);

        foreach (uint entry in relocationEntries)
        {
            rel.AddRelocation(entry, Relocations.BASE_RELOCATION_TYPE.HIGHLOW);
        }
        return rel.WriteRelocations();
    }

    private void UpdateAsmBlockInputs()
    {
        bool useX = AsmBlockTypeCombo.SelectedIndex is 0 or 1 or 2;
        bool useY =  AsmBlockTypeCombo.SelectedIndex is 0 or 1;
        bool useZ1 = AsmBlockTypeCombo.SelectedIndex is 3;
        bool useZ2 = AsmBlockTypeCombo.SelectedIndex is 4;

        AsmBlockUnitCombo.IsEnabled = useX;
        AsmBlockValueInput.IsEnabled = useY;
        AsmBlockTechCombo.IsEnabled = useZ1;
        AsmBlockBuildingCombo.IsEnabled = useZ2;
    }
}