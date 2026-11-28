# KAN-74 연쇄 타격 대상 선택

`ChainTargetSelector.FindAdditionalTargets`에 이미 타격한 최초 대상, 최대 추가 타격 수, 단계 최대 거리, 후보 스냅샷을 전달한다. 반환 순서대로 타격하며 최초 타격은 반환값에 포함되지 않는다. 최초 타격으로 대상이 죽어도 위치와 ID가 유효하면 연쇄 기준점으로 사용한다. 다른 공격에서 호출하면 새 연쇄가 시작된다. [KAN-74](https://gametithe.atlassian.net/browse/KAN-74) · [KAN-52](https://gametithe.atlassian.net/browse/KAN-52)

매 단계는 직전에 선택한 좌표에서 최근접 유효 대상을 찾는다. 사거리 경계를 포함하며 동거리에는 ID의 서수 순서를 사용한다. 이전 대상의 콜라이더를 동일 ID로 반복해 전달해도 한 연쇄에서 한 번만 타격한다. 후보 ID에는 실제 대상에 귀속된 안정적 ID를 사용한다. 나머지 대상이 모두 사거리 밖이거나 무효이면 종료한다.

EditMode에서 `ProjectWeapon.Combat.Tests.ChainTargetSelectorTests`를 실행하면 새 원점, 최대 타격 수, 죽은 최초 대상, 중복 콜라이더, 동거리 역순, 0 거리, 경계와 무효 후보를 재현한다. 최대 타격 수·사거리는 무기 쪽 호출자가 전달한다.
