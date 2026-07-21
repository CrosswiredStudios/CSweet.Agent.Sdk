import unittest
from types import SimpleNamespace

from csweet_agent_sdk import AgentIdentity


class AgentIdentityTests(unittest.TestCase):
    def test_maps_employee_identity_from_registration(self) -> None:
        registration = SimpleNamespace(
            employee_identity=SimpleNamespace(
                employee_id="employee-1",
                display_name="Avery",
                role_id="role-1",
                role_name="Operations Lead",
                role_description="Own operations.",
                role_responsibilities=["Coordinate delivery"],
                authority_level="ExecutionWithApproval",
                manager_employee_id="manager-1",
                manager_display_name="Morgan",
            )
        )

        identity = AgentIdentity.from_registration(registration)

        self.assertIsNotNone(identity)
        self.assertEqual("Avery", identity.display_name)
        self.assertEqual(("Coordinate delivery",), identity.role_responsibilities)
        self.assertEqual("Morgan", identity.manager_display_name)

    def test_missing_employee_identity_is_backward_compatible(self) -> None:
        self.assertIsNone(AgentIdentity.from_registration(SimpleNamespace()))


if __name__ == "__main__":
    unittest.main()
