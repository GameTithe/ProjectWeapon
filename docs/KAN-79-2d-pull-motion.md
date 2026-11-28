# KAN-79 2D 흡인 이동

`PullMotion.TryStart`는 유효한 Rigidbody2D 대상, 고정 중심, 사거리·속도·지속 시간과 벽 레이어를 받는다. 기존 대상 소유자가 FixedUpdate마다 `Advance`의 이동 벡터를 사용한다. Dynamic 대상은 해당 스텝에 사용할 속도로 바꾸고, Kinematic 대상은 작은 `MovePosition` 이동을 사용한다. 마지막 프레임에 일부 이동이 있으면 반영한 후 다음 단계의 제어권을 대상 모터로 돌린다. [KAN-79](https://gametithe.atlassian.net/browse/KAN-79) · [Unity 6000.6 Collider2D.Cast](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/collider2d/cast) · [Unity 6000.6 Rigidbody2D.MovePosition](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/rigidbody2d/moveposition)

기존 Collider2D 형태로 앞을 검사하고, 지정한 물리 벽의 최초 접촉 직전까지 움직인다. 대상의 감지용 트리거와 경로에 놓인 트리거는 제외한다. 사거리 경계 포함, 유효성 상실, 중심 도착, 충돌, 마지막 부분 스텝을 독립 Legacy 2D 물리 씬에서 재현한다. 구체적인 스킬별 범위와 속도는 해당 스킬 자산이 제공한다.
