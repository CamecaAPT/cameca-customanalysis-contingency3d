using Cameca.CustomAnalysis.Interface;
using Cameca.CustomAnalysis.Utilities;
using Cameca.CustomAnalysis.Utilities.Legacy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Documents;

namespace Cameca.CustomAnalysis.ContingencyTable3D;

internal class ContingencyTable3DAnalysis : ICustomAnalysis<ContingencyTable3DOptions>
{
    //Node ID
    public Guid ID { get; set; }

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

        //Output the outBuilder string
        viewBuilder.AddText("3DCT Output", outBuilder.ToString());
    }

    private static string GetBasicIonInfo(IIonData ionData, IViewBuilder viewBuilder)
    {
        StringBuilder outBuilder = new();
        List<RangeCountRow> RangeCountRows = new();
        var typeCounts = ionData.GetIonTypeCounts();
        ulong totalRangedIons = 0;
        for(int i=0; i<typeCounts.Count; i++)
        {
            var thisIon = typeCounts.ElementAt(i);
            totalRangedIons += thisIon.Value;
            RangeCountRows.Add(new RangeCountRow(i + 1, thisIon.Key.Name, thisIon.Value));
            outBuilder.AppendLine($"range {i + 1}: {thisIon.Key.Name} \t=\t{thisIon.Value}");
        }
        outBuilder.AppendLine($"Total Ions \t=\t{ionData.IonCount}");
        outBuilder.AppendLine();
        outBuilder.AppendLine($"Ions in ranges = {totalRangedIons}, total events = {ionData.IonCount}");

        viewBuilder.AddTable("Range and Ion Info", RangeCountRows);
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