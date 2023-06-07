using Cameca.CustomAnalysis.Interface;
using Cameca.CustomAnalysis.Utilities;
using Cameca.CustomAnalysis.Utilities.Legacy;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Windows;
using System.Windows.Controls.Primitives;
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
         * Basic Calculations
         */
        ulong totalRangedIons = 0;
        foreach (ulong ionCount in ionData.GetIonTypeCounts().Values)
            totalRangedIons += ionCount;
        var min = ionData.Extents.Min;
        var max = ionData.Extents.Max;
        Vector3 diff = max - min;
        double volume = diff.X * diff.Y * diff.Z;
        double spacing = Math.Pow(volume * options.BlockSize / totalRangedIons, 1.0 / 3.0); //take cube root of volume per block to get length of block
        int numGridX = (int)(diff.X / spacing) + 1;
        int numGridY = (int)(diff.Y / spacing) + 1;
        int gridElements = numGridX * numGridY;
        int rows = (options.BlockSize + 1) / options.BinSize;
        if ((options.BlockSize + 1) % options.BinSize > 0)
            rows++;
        int columns = rows;

        /*
         * Get Total Blocks
         */
        var totalBlocks = GetTotalBlocks(ionData, options.BlockSize, min, numGridX, numGridY, spacing);

        /*
         * Contingency Main
         */
        outBuilder.AppendLine(CalculateContingencyTables(ionData, min, numGridX, numGridY, spacing, options.BlockSize, totalBlocks, rows, options.BinSize, viewBuilder));

        //Output the outBuilder string
        viewBuilder.AddText("3DCT Output", outBuilder.ToString());
    }

    private static DataTable InitDataTable(int size)
    {
        DataTable dataTable = new();

        string columnName = "";
        for(int i = 0; i < size + 2; i++)
        {
            columnName += " ";
            dataTable.Columns.Add(columnName);
        }

        return dataTable;
    }

    //individual contingnecy tables are per range combination (1 for Fe and Ni, 1 for Fe and Cu, etc)
    private static string CalculateContingencyTables(IIonData ionData, Vector3 min, int numGridX, int numGridY, double spacing, int blockSize, int totalBlocks, int rows, int binSize, IViewBuilder viewBuilder)
    {
        StringBuilder outBuilder = new();

        string[] requiredSections = new string[] { IonDataSectionName.Position, IonDataSectionName.IonType };

        var numIonsTypes = ionData.GetIonTypeCounts().Count;
        string[] ionNames = new string[numIonsTypes];
        int index = 0;
        foreach (var ionName in ionData.GetIonTypeCounts().Keys)
        {
            ionNames[index] = ionName.Name;
            index++;
        }

        for (int ionType1 = 0; ionType1 < numIonsTypes; ionType1++)
        {
            for (int ionType2 = ionType1 + 1; ionType2 < numIonsTypes; ionType2++)
            {
                //these are the amount of blocks
                int[] type1InBlock = new int[totalBlocks + 1];
                int[] type2InBlock = new int[totalBlocks + 1];

                foreach (var chunk in ionData.CreateSectionDataEnumerable(requiredSections))
                {
                    var positions = chunk.ReadSectionData<Vector3>(IonDataSectionName.Position).Span;
                    var ionTypes = chunk.ReadSectionData<byte>(IonDataSectionName.IonType).Span;

                    //these are all the amount of grid elements
                    int[,] ionGrid = new int[numGridX, numGridY];
                    int[,] type1Grid = new int[numGridX, numGridY];
                    int[,] type2Grid = new int[numGridX, numGridY];

                    //could try to remove data from positions (read only span, would need to copy somehow. may defeat purpose)
                    for (int ionIndex = 0, blockIndex = 0; ionIndex < ionTypes.Length; ionIndex++)
                    {
                        byte elementType = ionTypes[ionIndex];
                        if (elementType == 255) continue;

                        int ionX = (int)((positions[ionIndex].X - min.X) / spacing);
                        int ionY = (int)((positions[ionIndex].Y - min.Y) / spacing);

                        if (elementType == ionType1) type1Grid[ionX, ionY]++;
                        if (elementType == ionType2) type2Grid[ionX, ionY]++;
                        ionGrid[ionX, ionY]++;

                        if (ionGrid[ionX, ionY] >= blockSize)
                        {
                            type1InBlock[blockIndex] = type1Grid[ionX, ionY];
                            type2InBlock[blockIndex] = type2Grid[ionX, ionY];
                            blockIndex++;
                            ionGrid[ionX, ionY] = 0;
                            type1Grid[ionX, ionY] = 0;
                            type2Grid[ionX, ionY] = 0;
                        }
                    }
                }
                outBuilder.AppendLine(CalculateContingencyTable(rows, rows, type1InBlock, type2InBlock, totalBlocks, binSize, ionNames, ionType1, ionType2, blockSize, viewBuilder));
            }
        }

        return outBuilder.ToString();
    }

    private static string CalculateContingencyTable(int rows, int columns, int[] type1InBlock, int[] type2InBlock, int totalBlocks, int binSize, string[] ionNames, int ionType1, int ionType2, int blockSize, IViewBuilder viewBuilder)
    {
        StringBuilder sb = new();
        DataTable dataTable = InitDataTable(rows);

        double[,] experimentalArr = new double[rows, columns];
        int totalObservations = 0;

        for(int i=0; i<totalBlocks; i++)
        {
            experimentalArr[type1InBlock[i] / binSize, type2InBlock[i] / binSize]++;
        }

        //marginal totals
        int[] marginalTotalsRows = new int[rows];
        int[] marginalTotalsCols = new int[columns];
        for(int row = 0; row < rows; row++)
        {
            for(int col = 0; col < columns; col++)
            {
                marginalTotalsRows[row] += (int)experimentalArr[row, col];
                marginalTotalsCols[col] += (int)experimentalArr[row, col];
                totalObservations += (int)experimentalArr[row, col];
            }
        }
        if(totalObservations <= 0)
        {
            //TODO: do something
        }

        (var message, var non0Rows, var non0Cols) = PrintTable(ionNames, ionType1, ionType2, rows, binSize, blockSize, experimentalArr, dataTable, marginalTotalsRows, marginalTotalsCols, totalObservations, "Experimental Observations");
        sb.AppendLine(message);

        //calculate estimated observations
        double[,] estimatedArr = new double[rows, columns];
        for (int row = 0; row < rows; row++)
        {
            for(int col = 0; col < columns; col++)
            {
                estimatedArr[row, col] = (double) (marginalTotalsRows[row] * marginalTotalsCols[col]) / totalObservations;
            }
        }
        (message, _, _) = PrintTable(ionNames, ionType1, ionType2, rows, binSize, blockSize, estimatedArr, dataTable, "Estimated Values");
        sb.AppendLine(message);

        //calculate X-square
        sb.AppendLine(CalculateXSquare(rows, experimentalArr, estimatedArr, non0Rows, non0Cols));

        //calculate and output difference values
        double[,] differenceArr = new double[rows, columns];
        for(int row = 0; row < rows; row++)
        {
            for(int col = 0; col < columns; col++)
            {
                differenceArr[row, col] = experimentalArr[row, col] - estimatedArr[row, col];
            }
        }
        (message, _, _) = PrintTable(ionNames, ionType1, ionType2, rows, binSize, blockSize, differenceArr, dataTable, "Difference Values");
        sb.AppendLine(message);

        //Do Trend Analysis
        sb.AppendLine(TrendAnalysis(differenceArr, dataTable));

        viewBuilder.AddTable($"{ionNames[ionType1]} vs {ionNames[ionType2]}", dataTable.DefaultView);
        return sb.ToString();
    }

    private static string TrendAnalysis(double[,] differenceArr, DataTable dataTable)
    {
        StringBuilder sb = new();

        sb.AppendLine("Trend Analysis");
        object[] rowArray = new object[differenceArr.GetLength(0) + 2];
        rowArray[0] = "Trend Analysis";
        //set up columns
        sb.Append("\t");
        for (int col = 0; col < differenceArr.GetLength(1); col++)
        {
            sb.Append($"{col}\t");
            rowArray[col + 1] = col;
        }
        sb.AppendLine();
        dataTable.Rows.Add(rowArray);
        Array.Clear(rowArray);

        for (int row = 0; row < differenceArr.GetLength(0); row++)
        {
            sb.Append($"{row}\t");
            rowArray[0] = row;
            for(int col = 0; col < differenceArr.GetLength(1); col++)
            {
                string output;
                if (differenceArr[row, col] < 0)
                    output = "-";
                else if (differenceArr[row, col] > 0)
                    output = "+";
                else
                    output = " ";
                sb.Append($"{output}\t");
                rowArray[col + 1] = output;
            }
            sb.AppendLine();
            dataTable.Rows.Add(rowArray);
            Array.Clear(rowArray);
        }

        return sb.ToString();
    }

    private static string CalculateXSquare(int rows, double[,] experimentalArr, double[,] estimatedArr, int non0Rows, int non0Cols)
    {
        StringBuilder sb = new();
        double xSquare = 0;

        for (int row = 0; row < rows; row++)
        {
            for(int col = 0; col < rows; col++)
            {
                if (estimatedArr[row, col] > 0.01) //this is baked into M. K. Miller's Script
                    xSquare += ((experimentalArr[row, col] - estimatedArr[row, col]) * (experimentalArr[row, col] - estimatedArr[row, col])) / estimatedArr[row, col];
            }
        }

        int degreesOfFreedom = (rows - 1) * (rows - 1);
        int reduced = (non0Cols - 1) * (non0Rows - 1);

        sb.AppendLine($"X-square = {xSquare.ToString($"f{ROUNDING_LENGTH}")} with {degreesOfFreedom} degrees of freedom (reduced = {reduced})");

        return sb.ToString();
    }

    private static (string, int, int) PrintTable(string[] ionNames, int ionType1, int ionType2, int rows, int binSize, int blockSize, double[,] dataArray, DataTable dataTable, string title)
    {
        return PrintTable(ionNames, ionType1, ionType2, rows, binSize, blockSize, dataArray, dataTable, null, null, -1, title);
    }

    private static (string, int, int) PrintTable(string[] ionNames, int ionType1, int ionType2, int rows, int binSize, int blockSize, double[,] dataArray, DataTable dataTable, int[]? marginalTotalRows, int[]? marginalTotalCols, int totalObservations, string title)
    {
        StringBuilder sb = new();
        int non0Cols = 0;
        int non0Rows = 0;

        sb.AppendLine($"{title}");
        sb.Append($"\t{ionNames[ionType2]}\n{ionNames[ionType1]}\t");
        //object[] rowArray = new object[] { title };
        //dataTable.Rows.Add(rowArray);
        object[] rowArray = new object[] { title , ionNames[ionType2] };
        dataTable.Rows.Add(rowArray);
        rowArray = new object[rows + 2];
        rowArray[0] = ionNames[ionType1];

        //set up columns (square matrix, thats why row and col is interchangable)
        for (int col = 0; col < rows; col++)
        {
            int startIndex = col * binSize;
            int endIndex = Math.Min(((col + 1) * binSize) - 1, blockSize);
            sb.Append($"{startIndex}-{endIndex}\t");
            rowArray[col + 1] = $"{startIndex}-{endIndex}";
        }
        if (marginalTotalRows != null)
        { 
            sb.Append("total");
            rowArray[rowArray.Length - 1] = "total";
        }
        sb.AppendLine();
        dataTable.Rows.Add(rowArray);
        Array.Clear(rowArray);

        for(int row = 0; row < rows; row++)
        {
            rowArray = new object[rows + 2];
            int startIndex = row * binSize;
            int endIndex = Math.Min(((row + 1) * binSize) - 1, blockSize);
            sb.Append($"{startIndex}-{endIndex}\t");
            rowArray[0] = $"{startIndex}-{endIndex}";
            for(int col = 0; col < rows; col++)
            {
                string formatString = (marginalTotalCols == null) ? "f1" : "";
                sb.Append($"{dataArray[row, col].ToString($"{formatString}")}\t");
                rowArray[col + 1] = dataArray[row, col].ToString(formatString);
            }
            if (marginalTotalRows != null && marginalTotalRows[row] > 0) 
                non0Rows++;
            if (marginalTotalRows != null)
            {
                sb.Append($"{marginalTotalRows[row]}");
                rowArray[rowArray.Length - 1] = marginalTotalRows[row];
            }
            sb.AppendLine();
            dataTable.Rows.Add(rowArray);
            Array.Clear(rowArray);
        }
        if (marginalTotalRows != null && marginalTotalCols != null)
        {
            sb.Append($"total\t");
            rowArray[0] = "total";
            for (int col = 0; col < rows; col++)
            {
                if (marginalTotalCols[col] > 0)
                    non0Cols++;
                sb.Append($"{marginalTotalCols[col]}\t");
                rowArray[col + 1] = marginalTotalCols[col];
            }
            sb.AppendLine($"{totalObservations}");
            rowArray[rowArray.Length - 1] = totalObservations;
            dataTable.Rows.Add(rowArray);
            Array.Clear(rowArray);
        }
        dataTable.Rows.Add();
        return (sb.ToString(), non0Rows, non0Cols);
    }

    private static int GetTotalBlocks(IIonData ionData, int blockSize, Vector3 min, int numGridX, int numGridY, double spacing)
    {
        string[] requiredSections = new string[] { IonDataSectionName.Position, IonDataSectionName.IonType };

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
                if (ionGrid[ionX, ionY] == blockSize)
                {
                    totalBlocks++;
                    ionGrid[ionX, ionY] = 0;
                }
            }
        }

        return totalBlocks;
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
