using Drilling.Common.Managers;
using Drilling.Common.Interface;
using Drilling.Common.Motion;
using Drilling.Common.Alarm;
using Drilling.Common.InterLock;
using Drilling.Common.Station;

namespace Drilling.Common.Motion;

[CMotionControllerType("UMAC")]
internal sealed class CUmacMotion(IInterfaceManager? interfaceManager, int deviceNo = 0)
    : CMotionController("UMAC", interfaceManager, deviceNo)
{
    protected override string CommandPrefix => "UMAC";
}

