# KAN-158 용암 도끼 화염 범위 타격

[Jira KAN-158](https://gametithe.atlassian.net/browse/KAN-158) · [KAN-54 자동 기본 공격](https://gametithe.atlassian.net/browse/KAN-54)

`Assets/ProjectWeapon/Weapons/PrototypeExpansion/LavaAxe.asset`의 3등급 원형 `lava_axe`는 도끼 분류와 화염 범위 타격 하나를 연결한다. 도끼 분류의 직렬화 값은 6으로 잡았으며 KAN-145와 동일하다.

자동 공격이 사거리 안의 가장 가까운 유효 대상의 위치를 고정한다. 같은 요청 시각에 그 위치를 중심으로 원형 범위에 있는 유효 대상에게 한 번의 피해 묶음을 만든다. 범위 가장자리에 닿는 대상도 포함되며, 보조 대상은 플레이어에서의 최초 표적 사거리 밖에 있을 수 있다. 대상 중복·사망·공격 불가·자기 자신은 기존 범위 판정이 거른다. 선택한 대상이 적용 후보에서 사라졌으면 요청을 거절하며 쿨다운은 소비되지 않는다.

검증 수치는 `LavaAxeFireArea.asset`에서 바꿀 수 있다. 사거리 2.5, 반경 1.25, 즉시 피해 18, 쿨다운 1.45초다. 장착 기본 공격 호스트는 `BasicAttackRequest.Pattern`에서 `TargetCircularStrikePattern`을 받아 `TryCreateStrike`를 호출하고 반환된 `CircularStrike.Hits`를 기존 피해 수신자에게 적용한다. 검증에는 기존 자동 공격 스케줄러와 테스트용 대상 후보를 사용한다.

Unity Test Runner EditMode의 `TargetCircularStrikePatternTests`에서 실제 원형과 설정 에셋을 불러온다. 대상 선정, 원형 경계와 피해 시각, 중복과 쿨다운 경계, 유효 범위 밖 요청과 대상 소실, 귀환 중 중단을 확인한다. 이 가안은 일반 공격 하나의 검증 범위이며 원래 시작 무기 카탈로그와 화염 지속 효과, 스킬, 최종 시각 효과의 연결은 별 범위다.
