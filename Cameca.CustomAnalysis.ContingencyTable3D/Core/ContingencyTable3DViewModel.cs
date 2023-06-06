using System;
using Cameca.CustomAnalysis.Utilities;
using Cameca.CustomAnalysis.Utilities.Legacy;

namespace Cameca.CustomAnalysis.ContingencyTable3D.Core;

internal class ContingencyTable3DViewModel
    : LegacyCustomAnalysisViewModelBase<ContingencyTable3DNode, ContingencyTable3DAnalysis, ContingencyTable3DOptions>
{
    public const string UniqueId = "Cameca.CustomAnalysis.ContingencyTable3D.ContingencyTable3DViewModel";

    public ContingencyTable3DViewModel(IAnalysisViewModelBaseServices services, Func<IViewBuilder> viewBuilderFactory)
        : base(services, viewBuilderFactory)
    {
    }
}