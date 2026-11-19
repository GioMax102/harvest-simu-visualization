using UnityEngine;

namespace HarvestSim.Visualization
{
    public sealed class AgentView : MonoBehaviour
    {
        public void Initialize(AgentData agent) => gameObject.name = agent.Id;
        public void Render(AgentState state, Vector3 position)
        {
            transform.localPosition = position;
            float angle = state.Orientation switch { "E" => 90, "S" => 180, "O" => 270, _ => 0 };
            transform.localRotation = Quaternion.Euler(0, angle, 0);
        }
    }
}
