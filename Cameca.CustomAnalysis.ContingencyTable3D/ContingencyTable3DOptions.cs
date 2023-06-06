﻿using System.ComponentModel.DataAnnotations;
using Prism.Mvvm;

namespace Cameca.CustomAnalysis.ContingencyTable3D;

public class ContingencyTable3DOptions : BindableBase
{
    /* Example of a "Bindable" property. Properties of this form will notify the analysis that they have been changed.
    private int count;
    [Display(Name = "Ion Count")]
    // [Display(AutoGenerateField = false)]  // An example of how to hide the property from the property editor
    public int Count
    {
        get => count;
        set => SetProperty(ref count, value);
    }
    //*/
}