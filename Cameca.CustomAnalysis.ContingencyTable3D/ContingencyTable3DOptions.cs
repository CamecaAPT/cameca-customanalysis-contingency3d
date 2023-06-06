using System.ComponentModel.DataAnnotations;
using Prism.Mvvm;

namespace Cameca.CustomAnalysis.ContingencyTable3D;

public class ContingencyTable3DOptions : BindableBase
{
    private int blockSize;
    [Display(Name = "Block Size", Description = "[0-1000] (atoms)")]
    public int BlockSize
    {
        get => blockSize;
        set => SetProperty(ref blockSize, value);
    }

    private int binSize;
    [Display(Name = "Bin Size", Description = "Must be no greater than block size (ions)")]
    public int BinSize
    {
        get => binSize;
        set => SetProperty(ref binSize, value);
    }
}