using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace x2_prototype;

public class ReassignmentItem(string assignmentType, string nation, string power)
{
    public string AssignmentType { get; } = assignmentType;
    public string AssignmentNation { get; } = nation;
    public string AssignmentPower { get; } = power;
}
