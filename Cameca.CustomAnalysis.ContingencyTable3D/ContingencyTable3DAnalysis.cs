using Cameca.CustomAnalysis.Interface;
using Cameca.CustomAnalysis.Utilities;
using Cameca.CustomAnalysis.Utilities.Legacy;
using CommunityToolkit.HighPerformance;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
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
        (var message, var totalRangedIons) = GetBasicIonInfo(ionData, viewBuilder, options.Decomposing);
        outBuilder.AppendLine(message);

        /*
         * Get Limits
         */
        outBuilder.AppendLine(GetLimits(ionData, viewBuilder));

        /*
         * Basic Calculations
         */
        var min = ionData.Extents.Min;
        var max = ionData.Extents.Max;
        Vector3 diff = max - min;
        double volume = diff.X * diff.Y * diff.Z;
        double spacing = Math.Pow(volume * options.BlockSize / totalRangedIons, 1.0 / 3.0); //take cube root of volume per block to get length of block

        outBuilder.AppendLine($"Grid spacing = {spacing.ToString("f1")}");

        int numGridX = (int)(diff.X / spacing) + 1;
        int numGridY = (int)(diff.Y / spacing) + 1;
        int rows = (options.BlockSize + 1) / options.BinSize;
        if ((options.BlockSize + 1) % options.BinSize > 0)
            rows++;

        /*
         * Get Total Blocks
         */
        var totalBlocks = GetTotalBlocks(ionData, options.BlockSize, min, numGridX, numGridY, spacing, options.Decomposing);

        /*
         * Contingency Main
         */
        outBuilder.AppendLine(CalculateContingencyTables(ionData, min, numGridX, numGridY, spacing, options.BlockSize, totalBlocks, rows, options.BinSize, viewBuilder, options.Decomposing));

        //Output the outBuilder string
        viewBuilder.AddText("3DCT Output", outBuilder.ToString());
    }

    /// <summary>
    /// Method to initialize each data table asked to have the size + 2 of given
    /// This is to have extra space for the name of rows column and the total column
    /// </summary>
    /// <param name="size">Size of datatable to create</param>
    /// <returns>A DataTable object containing the column information required to fill the rest of it in</returns>
    private static DataTable InitDataTable(int size)
    {
        DataTable dataTable = new();

        string columnName = "";
        for(int i = 0; i < size + 2; i++)
        {
            columnName += " "; //each column must have a unique name (apparently) and so this way they're all uniquely empty
            dataTable.Columns.Add(columnName);
        }

        return dataTable;
    }

    /// <summary>
    /// Creates two dictionaries for conversion between the AP Suite provided Ion "IDs" and this extension's Ion "IDs".
    /// The first is a dictionary from bytes to a list of ints, which maps the AP Suite id to a list of this extension's ids. This is for
    /// if the program is set to be decomposing the ions.
    /// The second is a dictionary taking this extensions ids and mapping them to the atom names to which they correspond to. 
    /// </summary>
    /// <param name="ionData">IIonData object to read information from</param>
    /// <returns>Two dictionaries containing conversion information between AP Suite and this extension</returns>
    private static (Dictionary<byte, List<int>>, Dictionary<int, string>) GetConversionDicts(IIonData ionData)
    {
        //AP Suite byte to list of MY indices
        Dictionary<byte, List<int>> apIndexToMyIndex = new();

        //atom name to my index thing
        Dictionary<string, int> nameToIndexDict = new();
        Dictionary<int, string> indexToNameDict = new();

        int index = 0;
        var ionTypeInformationList = ionData.GetIonTypeCounts().Keys;
        foreach(var ionTypeInfo in ionTypeInformationList)
        {
            var enumerator = ionTypeInfo.Formula.GetEnumerator();
            while( enumerator.MoveNext())
            {
                var curr = enumerator.Current;
                if(!nameToIndexDict.ContainsKey(curr.Key))
                {
                    nameToIndexDict.Add(curr.Key, index);
                    indexToNameDict.Add(index, curr.Key);
                    index++;
                }
            }
        }

        byte apIndex = 0;
        foreach(var ionTypeInfo in ionTypeInformationList)
        {
            List<int> atomList = new();
            var enumerator = ionTypeInfo.Formula.GetEnumerator();
            while(enumerator.MoveNext())
            {
                var curr = enumerator.Current;
                for(int i=0; i<curr.Value; i++)
                {
                    atomList.Add(nameToIndexDict[curr.Key]);
                }
            }
            apIndexToMyIndex.Add(apIndex, atomList);
            apIndex++;
        }

        return (apIndexToMyIndex, indexToNameDict);
    }

    /// <summary>
    /// Main contingency table method of this extension. This method is the main way that the extension looks into the actual ion data.
    /// In charge of calculating and displaying the contingency tables for this dataset. 
    /// </summary>
    /// <param name="ionData">IIonData object to look at various ion data information</param>
    /// <param name="min">Vector object holding the smaller bound of the dataset</param>
    /// <param name="numGridX">Number of grid elements in the X direction</param>
    /// <param name="numGridY">Number of grid elements in the Y direction</param>
    /// <param name="spacing">length of each block as computed from volume per block</param>
    /// <param name="blockSize">The amount of ions allowed in any given block before it "overflows"</param>
    /// <param name="totalBlocks">The amount of blocks this dataset uses</param>
    /// <param name="rows">The number of rows in the contingency tables</param>
    /// <param name="binSize">The number of ions per bin</param>
    /// <param name="viewBuilder">IViewBuilder object to dipslay the information into tables</param>
    /// <returns>A formatted string of all of the contingency tables to be output to the "console" view</returns>
    private static string CalculateContingencyTables(IIonData ionData, Vector3 min, int numGridX, int numGridY, double spacing, int blockSize, int totalBlocks, int rows, int binSize, IViewBuilder viewBuilder, bool isDecomposing)
    {
        StringBuilder outBuilder = new();

        string[] requiredSections = new string[] { IonDataSectionName.Position, IonDataSectionName.IonType };

        string[] ionNames;
        (var apIndexToMyIndex, var indexToNameDict) = GetConversionDicts(ionData);
        if (isDecomposing)
        {
            ionNames = new string[indexToNameDict.Count];
            foreach(var ionName in indexToNameDict)
            {
                ionNames[ionName.Key] = ionName.Value;
            }
        }
        else
        {
            ionNames = new string[ionData.GetIonTypeCounts().Count];
            int index = 0;
            foreach (var ionName in ionData.GetIonTypeCounts().Keys)
            {
                ionNames[index] = ionName.Name;
                index++;
            }
        }

        //go through all ions once, populate tables, then compare each ion type
        int[,] ionGrid = new int[numGridX, numGridY];
        int[,,] typeGrid = new int[ionNames.Length, numGridX, numGridY];
        int[,] typeBlock = new int[ionNames.Length, totalBlocks + 1];
        int blockIndex = 0;
        foreach (var chunk in ionData.CreateSectionDataEnumerable(requiredSections))
        {
            var positions = chunk.ReadSectionData<Vector3>(IonDataSectionName.Position).Span;
            var ionTypes = chunk.ReadSectionData<byte>(IonDataSectionName.IonType).Span;

            for (int ionIndex = 0; ionIndex < ionTypes.Length; ionIndex++)
            {
                byte simpleElementType = ionTypes[ionIndex];
                if (simpleElementType == 255) continue;

                Queue<int> ionQueue = new();

                //if ion is multiatom
                if (isDecomposing)
                {
                    foreach (var myIonIndex in apIndexToMyIndex[simpleElementType])
                    {
                        ionQueue.Enqueue(myIonIndex);
                    }
                }
                else
                    ionQueue.Enqueue(simpleElementType);

                int ionX = (int)((positions[ionIndex].X - min.X) / spacing);
                int ionY = (int)((positions[ionIndex].Y - min.Y) / spacing);

                while (ionQueue.Count > 0)
                {
                    int elementType = ionQueue.Dequeue();


                    typeGrid[elementType, ionX, ionY]++;
                    ionGrid[ionX, ionY]++;

                    if (ionGrid[ionX, ionY] >= blockSize)
                    {
                        for (int i = 0; i < ionNames.Length; i++)
                        {
                            typeBlock[i, blockIndex] = typeGrid[i, ionX, ionY];
                            typeGrid[i, ionX, ionY] = 0;
                        }
                        blockIndex++;
                        ionGrid[ionX, ionY] = 0;
                    }
                }
            }
        }
        for (int ionType1 = 0; ionType1 < ionNames.Length; ionType1++)
        {
            for (int ionType2 = ionType1 + 1; ionType2 < ionNames.Length; ionType2++)
            {
                outBuilder.AppendLine(CalculateContingencyTable(rows, typeBlock.GetRow(ionType1).ToArray(), typeBlock.GetRow(ionType2).ToArray(), totalBlocks, binSize, ionNames, ionType1, ionType2, blockSize, viewBuilder));
            }
        }

        return outBuilder.ToString();
    }

    /// <summary>
    /// Method to Calculate and display information for a single contingency table (two different ion types)
    /// </summary>
    /// <param name="rows">The number of rows in the contingency table</param>
    /// <param name="type1InBlock">integer array containing the counts of ion type 1 in each block</param>
    /// <param name="type2InBlock">integer array containing the counts of ion type 2 in each block</param>
    /// <param name="totalBlocks">the total number of blocks in this dataset</param>
    /// <param name="binSize">The number of ions per bin</param>
    /// <param name="ionNames">An array mapping the ion type "id" to the string value of its name</param>
    /// <param name="ionType1">An integer containing the id of the first ion</param>
    /// <param name="ionType2">An integer containing the id of the second ion</param>
    /// <param name="blockSize">The number of ions per block</param>
    /// <param name="viewBuilder">IViewBuilder object to chart the results to AP Suite</param>
    /// <returns>A formatted string containing all the contingency table data (tables) for this specific set of ions</returns>
    private static string CalculateContingencyTable(int rows, int[] type1InBlock, int[] type2InBlock, int totalBlocks, int binSize, string[] ionNames, int ionType1, int ionType2, int blockSize, IViewBuilder viewBuilder)
    {
        StringBuilder sb = new();
        DataTable dataTable = InitDataTable(rows);

        double[,] experimentalArr = new double[rows, rows];
        int totalObservations = 0;

        for(int i=0; i<totalBlocks; i++)
        {
            experimentalArr[type1InBlock[i] / binSize, type2InBlock[i] / binSize]++;
        }

        //marginal totals
        int[] marginalTotalsRows = new int[rows];
        int[] marginalTotalsCols = new int[rows];
        for(int row = 0; row < rows; row++)
        {
            for(int col = 0; col < rows; col++)
            {
                marginalTotalsRows[row] += (int)experimentalArr[row, col];
                marginalTotalsCols[col] += (int)experimentalArr[row, col];
                totalObservations += (int)experimentalArr[row, col];
            }
        }

        (var message, var non0Rows, var non0Cols) = PrintTable(ionNames, ionType1, ionType2, rows, binSize, blockSize, experimentalArr, dataTable, marginalTotalsRows, marginalTotalsCols, totalObservations, "Experimental Observations");
        sb.AppendLine(message);

        //calculate estimated observations
        double[,] estimatedArr = new double[rows, rows];
        for (int row = 0; row < rows; row++)
        {
            for(int col = 0; col < rows; col++)
            {
                estimatedArr[row, col] =  ((double)marginalTotalsRows[row] * marginalTotalsCols[col]) / totalObservations;
            }
        }
        (message, _, _) = PrintTable(ionNames, ionType1, ionType2, rows, binSize, blockSize, estimatedArr, dataTable, "Estimated Values");
        sb.AppendLine(message);

        //calculate X-square
        sb.AppendLine(CalculateXSquare(rows, experimentalArr, estimatedArr, non0Rows, non0Cols));

        //calculate and output difference values
        double[,] differenceArr = new double[rows, rows];
        for(int row = 0; row < rows; row++)
        {
            for(int col = 0; col < rows; col++)
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

    /// <summary>
    /// Method to run the trend analysis, which essentially just shows if theres more or less ions than expected for a given bin and block
    /// </summary>
    /// <param name="differenceArr">An array containing the differences in the blocks and bins</param>
    /// <param name="dataTable">DataTable object to add row and overall table information to to display to user</param>
    /// <returns>A formatted string showing the trend analysis table in the "console" view</returns>
    private static string TrendAnalysis(double[,] differenceArr, DataTable dataTable)
    {
        StringBuilder sb = new();

        sb.AppendLine("Trend Analysis");
        object[] rowArray = new object[differenceArr.GetLength(0) + 2];
        rowArray[0] = "Trend Analysis";
        //set up columns
        sb.Append('\t');
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

    /// <summary>
    /// Method to calculate X-square of a given contingency table
    /// </summary>
    /// <param name="rows">Number of rows in the contingency table</param>
    /// <param name="experimentalArr">The array of actual, experimental data</param>
    /// <param name="estimatedArr">The array of data if everything was spread out evenly / randomly</param>
    /// <param name="non0Rows">The number of non zero rows in this table</param>
    /// <param name="non0Cols">The number of non zero columns in this table</param>
    /// <returns>A formatted string to display the relevent X-squared data</returns>
    private static string CalculateXSquare(int rows, double[,] experimentalArr, double[,] estimatedArr, int non0Rows, int non0Cols)
    {
        double[] P = { 0.250f, 0.1f, 0.05f, 0.025f, 0.01f, 0.005f, 0.001f };
        double[] X = { 0.6745f, 1.2816f, 1.6449f, 1.96f, 2.3263f, 2.5758f, 3.0902f };

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

        if(reduced > 0)
        {
            for(int i=0; i<X.Length; i++)
            {
                double value = .5 * (X[i] + Math.Sqrt(2 * reduced - 1)) * (X[i] + Math.Sqrt(2 * reduced - 1));
                sb.Append($"P({P[i].ToString($"f{ROUNDING_LENGTH}")}) = {value.ToString($"f{ROUNDING_LENGTH - 1}")}");
                if (i < X.Length - 1)
                    sb.Append(", ");
            }
        }
        sb.AppendLine();

        return sb.ToString();
    }

    /// <summary>
    /// Print method to display a given table from one of the possible contingency table types (not experimental data)
    /// </summary>
    /// <param name="ionNames">Array of strings mapping the ion id to the string name of it</param>
    /// <param name="ionType1">Integer value of the id of ion type 1</param>
    /// <param name="ionType2">Integer value of the id of ion type 2</param>
    /// <param name="rows">Number of rows in this given table</param>
    /// <param name="binSize">The number of ions per bin</param>
    /// <param name="blockSize">The number of ions per block</param>
    /// <param name="dataArray">The array to be printed in table format</param>
    /// <param name="dataTable">A DataTable object to display this table into</param>
    /// <param name="title">Title of the table being displayed</param>
    /// <returns></returns>
    private static (string, int, int) PrintTable(string[] ionNames, int ionType1, int ionType2, int rows, int binSize, int blockSize, double[,] dataArray, DataTable dataTable, string title)
    {
        return PrintTable(ionNames, ionType1, ionType2, rows, binSize, blockSize, dataArray, dataTable, null, null, -1, title);
    }

    /// <summary>
    /// General print method to display any of the possible contingency table types
    /// </summary>
    /// <param name="ionNames">Array of strings mapping the ion id to the string name of it</param>
    /// <param name="ionType1">Integer value of the id of ion type 1</param>
    /// <param name="ionType2">Integer value of the id of ion type 2</param>
    /// <param name="rows">Number of rows in this given table</param>
    /// <param name="binSize">The number of ions per bin</param>
    /// <param name="blockSize">The number of ions per block</param>
    /// <param name="dataArray">The array to be printed in table format</param>
    /// <param name="dataTable">A DataTable object to display this table into</param>
    /// <param name="marginalTotalRows">Integer array of the totals of the rows</param>
    /// <param name="marginalTotalCols">Integer array of the totals of the columns</param>
    /// <param name="totalObservations">Total number of ions "observed" in this given table</param>
    /// <param name="title">Title of the table being displayed</param>
    /// <returns>A formatted string displaying the table for the "console" view</returns>
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

    /// <summary>
    /// Calculates the total number of blocks in this given dataset given the block size and bin size
    /// </summary>
    /// <param name="ionData">IIonData object to look at the actual ion data</param>
    /// <param name="blockSize">Number of ions per block</param>
    /// <param name="min">Vector3 object containing the smaller bound of the dataset</param>
    /// <param name="numGridX">Number of grid elements in the X direction</param>
    /// <param name="numGridY">Number of grid elements in the Y direction</param>
    /// <param name="spacing">length of each block as computed from volume per block</param>
    /// <returns>The total number of blocks in this dataset given the block and bin size</returns>
    private static int GetTotalBlocks(IIonData ionData, int blockSize, Vector3 min, int numGridX, int numGridY, double spacing, bool isDecomposing)
    {
        string[] requiredSections = new string[] { IonDataSectionName.Position, IonDataSectionName.IonType };

        int[,] ionGrid = new int[numGridX, numGridY];
        int totalBlocks = 0;

        foreach (var chunk in ionData.CreateSectionDataEnumerable(requiredSections))
        {
            var positions = chunk.ReadSectionData<Vector3>(IonDataSectionName.Position).Span;
            var ionTypes = chunk.ReadSectionData<byte>(IonDataSectionName.IonType).Span;

            (var apIndexToMyIndex, var indexToNameDict) = GetConversionDicts(ionData);
            for (int ionIndex = 0; ionIndex < ionTypes.Length; ionIndex++)
            {
                byte simpleElementType = ionTypes[ionIndex];
                if (simpleElementType == 255) continue;

                Queue<int> ionQueue = new();

                //if ion is multiatom
                if (isDecomposing)
                {
                    foreach (var myIonIndex in apIndexToMyIndex[simpleElementType])
                    {
                        ionQueue.Enqueue(myIonIndex);
                    }
                }
                else
                    ionQueue.Enqueue(simpleElementType);

                int ionX = (int)((positions[ionIndex].X - min.X) / spacing);
                int ionY = (int)((positions[ionIndex].Y - min.Y) / spacing);

                while (ionQueue.Count > 0)
                {
                    int elementType = ionQueue.Dequeue();
                    
                    ionGrid[ionX, ionY]++;

                    if (ionGrid[ionX, ionY] >= blockSize)
                    {
                        totalBlocks++;
                        ionGrid[ionX, ionY] = 0;
                    }
                }
            }
        }
        return totalBlocks;
    }

    /// <summary>
    /// Method to get the limits and return a string, along with creating a table for the information
    /// </summary>
    /// <param name="ionData">IIonData object to look at the ion information</param>
    /// <param name="viewBuilder">IViewBuilder object to display the bounds data to the table</param>
    /// <returns>A formatted string displaying the bounds of the dataset</returns>
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

    /// <summary>
    /// Gets and displays the basic ion info (total ions, types of ions and their counts)
    /// </summary>
    /// <param name="ionData">IIonData object to look at the ion information</param>
    /// <param name="viewBuilder">IViewBuilder object to display the basic ion data to the table</param>
    /// <returns></returns>
    private static (string, ulong) GetBasicIonInfo(IIonData ionData, IViewBuilder viewBuilder, bool isDecomposing)
    {
        StringBuilder outBuilder = new();
        List<RangeCountRow> rangeCountRows = new();
        (var apIndexToMyIndex, var indexToNameDict) = GetConversionDicts(ionData);
        var typeCounts = ionData.GetIonTypeCounts();
        ulong totalRangedIons = 0;
        for (int i = 0; i < typeCounts.Count; i++)
        {
            if(isDecomposing)
            {
                foreach(var myIonIndex in apIndexToMyIndex[(byte)i])
                {
                    totalRangedIons += typeCounts.ElementAt(i).Value;
                }
            }
            else
                totalRangedIons += typeCounts.ElementAt(i).Value;
        }

        if(isDecomposing)
        {
            //myIndex to count
            Dictionary<int, ulong> decomposedTypeCounts = new();
            for(int i = 0; i < typeCounts.Count; i++)
            {
                foreach(var myIonIndex in apIndexToMyIndex[(byte)i])
                {
                    if (!decomposedTypeCounts.ContainsKey(myIonIndex))
                        decomposedTypeCounts.Add(myIonIndex, 0);
                    decomposedTypeCounts[myIonIndex] += typeCounts.ElementAt(i).Value;
                }
            }

            int displayIndex = 1;
            foreach(var decomposedTypeCount in decomposedTypeCounts)
            {
                string percent = ((double)decomposedTypeCount.Value / totalRangedIons).ToString($"p{ROUNDING_LENGTH}");
                rangeCountRows.Add(new RangeCountRow(displayIndex, indexToNameDict[decomposedTypeCount.Key], decomposedTypeCount.Value, percent));
                outBuilder.AppendLine($"range {displayIndex}: {indexToNameDict[decomposedTypeCount.Key]} \t=\t{decomposedTypeCount.Value}");
                displayIndex++;
            }
        }
        else
        {
            for (int i = 0; i < typeCounts.Count; i++)
            {
                var thisIon = typeCounts.ElementAt(i);
                string percent = ((double)thisIon.Value / totalRangedIons).ToString($"p{ROUNDING_LENGTH}");
                rangeCountRows.Add(new RangeCountRow(i + 1, thisIon.Key.Name, thisIon.Value, percent));
                outBuilder.AppendLine($"range {i + 1}: {thisIon.Key.Name} \t=\t{thisIon.Value}");
            }
        }

        outBuilder.AppendLine($"Total Ions \t=\t{ionData.IonCount}");
        outBuilder.AppendLine();
        outBuilder.AppendLine($"Ions in ranges = {totalRangedIons}, total events = {ionData.IonCount}");

        viewBuilder.AddTable("Range and Ion Info", rangeCountRows);
        return (outBuilder.ToString(), totalRangedIons);
    }

    /// <summary>
    /// Checks user input for good input. Tells user if the input is not good
    /// </summary>
    /// <param name="blockSize">Ions per block</param>
    /// <param name="binSize">Ions per bin</param>
    /// <returns>a boolean value, true if input is good, false if input is bad</returns>
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
    public string Percent { get; set; }

    public RangeCountRow(int number, string name, ulong Count, string percent)
    {
        this.Number = number;
        this.Name = name;
        this.Count = Count;
        Percent = percent;
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
