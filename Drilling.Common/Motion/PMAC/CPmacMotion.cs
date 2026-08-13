using Drilling.Common.Managers;
using Drilling.Common.Interface;
using Drilling.Common.Motion;
using Drilling.Common.Alarm;
using Drilling.Common.InterLock;
using Drilling.Common.Station;

namespace Drilling.Common.Motion;

[CMotionControllerType("PMAC")]
internal sealed class CPmacMotion(IInterfaceManager? interfaceManager, int deviceNo = 0)
    : CMotionController("PMAC", interfaceManager, deviceNo)
{
    protected override string CommandPrefix => "PMAC";
}

