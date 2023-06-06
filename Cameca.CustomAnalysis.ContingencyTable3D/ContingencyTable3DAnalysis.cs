using Cameca.CustomAnalysis.Interface;
using Cameca.CustomAnalysis.Utilities;
using Cameca.CustomAnalysis.Utilities.Legacy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Windows;
using System.Windows.Documents;

namespace Cameca.CustomAnalysis.ContingencyTable3D;

internal class ContingencyTable3DAnalysis : ICustomAnalysis<ContingencyTable3DOptions>
{
    //Node ID
    public Guid ID { get; set; }

    //Constants
    const int ROUNDING_LENGTH = 3;

    /*
     * Services
     */
    //private readonly IMassSpectrumRangeManagerProvider _massSpectrumRangeManagerProvider;

    public ContingencyTable3DAnalysis(IMassSpectrumRangeManagerProvider massSpectrumRangeManagerProvider)
    {
        //_massSpectrumRangeManagerProvider = massSpectrumRangeManagerProvider;
    }


    /// <summary>
    /// Main custom analysis execution method.
    /// </summary>
    /// <remarks>
    /// Use <paramref name="ionData"/> as the data source for your calculation.
    /// Configurability in AP Suite can be implemented by creating editable properties in the options object. Access here with <paramref name="options"/>.
    /// Render your results with a variety of charts or tables by passing your final data to <see cref="IViewBuilder"/> methods.
    /// e.g. Create a histogram by calling <see cref="IViewBuilder.AddHistogram2D"/> on <paramref name="viewBuilder"/>
    /// </remarks>
    /// <param name="ionData">Provides access to mass, position, and other ion data.</param>
    /// <param name="options">Configurable options displayed in the property editor.</param>
    /// <param name="viewBuilder">Defines how the result will be represented in AP Suite</param>
    public void Run(IIonData ionData, ContingencyTable3DOptions options, IViewBuilder viewBuilder)
    {
        //Check user input for correctness
        if (!CheckUserInput(options.BlockSize, options.BinSize))
            return;

        StringBuilder outBuilder = new();

        /*
         * Get basic Ion info and Range info
         */
        outBuilder.AppendLine(GetBasicIonInfo(ionData, viewBuilder));

        /*
         * Get Limits
         */
        outBuilder.AppendLine(GetLimits(ionData, viewBuilder));

        /*
         * Make ion grid
         */
        MakeIonGrid(ionData, options.BlockSize, options.BinSize);

        //Output the outBuilder string
        viewBuilder.AddText("3DCT Output", outBuilder.ToString());
    }

    private static void MakeIonGrid(IIonData ionData, int ionsPerBlock, int ionsPerBin)
    {
        string[] requiredSections = new string[] { IonDataSectionName.Position, IonDataSectionName.IonType };

        var min = ionData.Extents.Min;
        var max = ionData.Extents.Max;
        Vector3 diff = max - min;
        ulong totalIons = 0;
        foreach(ulong ionCount in ionData.GetIonTypeCounts().Values)
            totalIons += ionCount;

        double volume = diff.X * diff.Y * diff.Z;
        double spacing = Math.Pow(volume * ionsPerBlock / totalIons, 1.0/3.0); //take cube root of volume per block to get length of block
        int rows = (ionsPerBlock + 1) / ionsPerBin;
        if((ionsPerBlock + 1) % ionsPerBin > 0)
            rows++;
        int columns = rows;

        int numGridX = (int)(diff.X / spacing) + 1;
        int numGridY = (int)(diff.Y / spacing) + 1;
        int gridElements = numGridX * numGridY;

        int[,] ionGrid = new int[numGridX, numGridY];
        int totalBlocks = 0;

        foreach (var chunk in ionData.CreateSectionDataEnumerable(requiredSections))
        {
            var positions = chunk.ReadSectionData<Vector3>(IonDataSectionName.Position).Span;
            var ionTypes = chunk.ReadSectionData<byte>(IonDataSectionName.IonType).Span;

            //get a count of the total amount of blocks
            for(int i = 0; i < positions.Length; i++)
            {
                if (ionTypes[i] == 255) continue;

                int ionX = (int)((positions[i].X - min.X) / spacing);
                int ionY = (int)((positions[i].Y - min.Y) / spacing);
                ionGrid[ionX, ionY]++;
                if (ionGrid[ionX, ionY] == ionsPerBlock)
                {
                    totalBlocks++;
                    ionGrid[ionX, ionY] = 0;
                }
            }
        }
    }

    private static string GetLimits(IIonData ionData, IViewBuilder viewBuilder)
    {
        StringBuilder outBuilder = new();
        List<LimitsRow> limitsRows = new();

        var Min = ionData.Extents.Min;
        var Max = ionData.Extents.Max;

        limitsRows.Add(new LimitsRow("X", Min.X.ToString($"f{ROUNDING_LENGTH}"), Max.X.ToString($"f{ROUNDING_LENGTH}")));
        limitsRows.Add(new LimitsRow("Y", Min.Y.ToString($"f{ROUNDING_LENGTH}"), Max.Y.ToString($"f{ROUNDING_LENGTH}")));
        limitsRows.Add(new LimitsRow("Z", Min.Z.ToString($"f{ROUNDING_LENGTH}"), Max.Z.ToString($"f{ROUNDING_LENGTH}")));

        outBuilder.AppendLine($"X limits: {Min.X.ToString($"f{ROUNDING_LENGTH}")} to {Max.X.ToString($"f{ROUNDING_LENGTH}")}");
        outBuilder.AppendLine($"Y limits: {Min.Y.ToString($"f{ROUNDING_LENGTH}")} to {Max.Y.ToString($"f{ROUNDING_LENGTH}")}");
        outBuilder.AppendLine($"Z limits: {Min.Z.ToString($"f{ROUNDING_LENGTH}")} to {Max.Z.ToString($"f{ROUNDING_LENGTH}")}");

        viewBuilder.AddTable("Limits", limitsRows);
        return outBuilder.ToString();
    }

    private static string GetBasicIonInfo(IIonData ionData, IViewBuilder viewBuilder)
    {
        StringBuilder outBuilder = new();
        List<RangeCountRow> rangeCountRows = new();
        var typeCounts = ionData.GetIonTypeCounts();
        ulong totalRangedIons = 0;
        for(int i=0; i<typeCounts.Count; i++)
        {
            var thisIon = typeCounts.ElementAt(i);
            totalRangedIons += thisIon.Value;
            rangeCountRows.Add(new RangeCountRow(i + 1, thisIon.Key.Name, thisIon.Value));
            outBuilder.AppendLine($"range {i + 1}: {thisIon.Key.Name} \t=\t{thisIon.Value}");
        }
        outBuilder.AppendLine($"Total Ions \t=\t{ionData.IonCount}");
        outBuilder.AppendLine();
        outBuilder.AppendLine($"Ions in ranges = {totalRangedIons}, total events = {ionData.IonCount}");

        viewBuilder.AddTable("Range and Ion Info", rangeCountRows);
        return outBuilder.ToString();
    }

    private static bool CheckUserInput(int blockSize, int binSize)
    {
        if (binSize > blockSize)
        {
            MessageBox.Show("Block size must be greater than or equal to bin size.");
            return false;
        }
        return true;
    }
}

public class RangeCountRow
{
    public int Number { get; set; }
    public string Name { get; set; }
    public ulong Count { get; set; }

    public RangeCountRow(int number, string name, ulong Count)
    {
        this.Number = number;
        this.Name = name;
        this.Count = Count;
    }
}

public class LimitsRow
{
    public string Dimension { get; set; }
    public string Min { get; set; }
    public string Max { get; set; }

    public LimitsRow(string dimension, string min, string max)
    {
        Dimension = dimension;
        Min = min;
        Max = max;
    }
}
