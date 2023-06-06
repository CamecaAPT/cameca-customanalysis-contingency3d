using Cameca.CustomAnalysis.Interface;
using Cameca.CustomAnalysis.Utilities;
using Cameca.CustomAnalysis.Utilities.Legacy;
using Prism.Events;
using Prism.Services.Dialogs;

namespace Cameca.CustomAnalysis.ContingencyTable3D.Core;

internal class ContingencyTable3DNodeMenuFactory : LegacyAnalysisMenuFactoryBase
{
    public ContingencyTable3DNodeMenuFactory(IEventAggregator eventAggregator, IDialogService dialogService)
        : base(eventAggregator, dialogService)
    {
    }

    protected override INodeDisplayInfo DisplayInfo => ContingencyTable3DNode.DisplayInfo;
    protected override string NodeUniqueId => ContingencyTable3DNode.UniqueId;
    public override AnalysisMenuLocation Location { get; } = AnalysisMenuLocation.Analysis;
}