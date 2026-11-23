# KAN-66 근거리 전리품 줍기

플레이어의 2D 감지 영역에 전리품이 들어오면 운반 슬롯 획득을 요청한다. 같은 실물의 여러 콜라이더와 반복 진입은 한 번의 시도로 취급한다. [KAN-66](https://gametithe.atlassian.net/browse/KAN-66) · [KAN-80 인벤토리 계약](KAN-80-inventory-capacity.md)

## 연결 방법

1. 플레이어의 전용 감지 객체에 `LootPickupTrigger`와 트리거 설정을 켠 `CircleCollider2D`를 둔다. 양쪽 중 한 콜라이더는 `Rigidbody2D`에 속해야 하며 두 레이어가 2D 물리에서 상호 감지돼야 한다. 트리거의 XY 월드 스케일은 균일하게 둔다. [Unity Collider 2D](https://docs.unity.com/en-us/engine/6000.6/manual/unity2d/2d-physics/collider-2d) · [Unity OnTriggerEnter2D](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/monobehaviour/ontriggerenter2d)
2. 탐험 조정 계층은 인벤토리로 만든 `LootPickupCoordinator`를 센서의 `Initialize`에 넘긴다. 동일 탐험 내에서 센서가 여럿이거나 다시 만들어지면 같은 조정 객체를 공유한다.
3. 전리품 루트에 `WorldLoot`를 붙이고 호출자가 만든 기존 실물 레코드로 초기화한다. 자식에도 감지용 콜라이더를 둘 수 있다. 진입 또는 체류 콜백에서 센서 중심과 전리품 루트의 XY 거리가 반경 이하면 한 번만 획득을 요청한다. 콜라이더가 먼저 겹치더라도 루트가 범위에 들어올 때까지 기다린다. [Unity OnTriggerStay2D](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/monobehaviour/ontriggerstay2d)

## 가방 선택과 재시도

- `OutcomeReported`의 전리품 객체, 실물 ID와 `InventoryPickupResult`를 월드 및 UI에 전달한다. `PickedUp`일 때만 획득 처리를 적용하며, 교체 시 반환된 `Displaced`를 호출자가 처리한다. 트리거는 월드 객체와 기존 무기를 자동 삭제하지 않는다.
- `ChoiceRequired`면 기존 월드 위치와 운반 물품을 유지한다. 구독자가 알림에서 받은 전리품 객체와 선택 값을 전달한다. UI 확인 시 `TryResolveChoice(worldLoot, choice, selectedId)`, 취소 시 선택 ID 자리에 `null`을 사용한다. 확인은 같은 실물이 활성 상태이고 센서 범위 안에 있을 때만 받는다. 범위를 벗어났을 때도 전리품 객체와 선택 ID를 모두 null로 보내 취소할 수 있다.
- 취소와 오래된 선택 이후에는 자동 재시도하지 않는다. UI 또는 탐험 조정 계층에서 `AllowRetry(id)`를 호출한 경우 다음 감지에서 다시 시도한다. 지원하지 않는 선택 ID는 원래 선택을 유지한다. 이미 획득하거나 인벤토리에 존재하던 ID는 같은 탐험 조정 객체 안에서 다시 시도하지 않는다.

## 재현 절차

Unity 프로젝트 루트의 PowerShell에서 실행한다.

```powershell
unity test . --mode EditMode --filter ProjectWeapon.Loot.Tests --output "$env:TEMP/KAN-66-editmode.xml" --timeout 900
unity test . --mode PlayMode --filter ProjectWeapon.Loot.PlayModeTests --output "$env:TEMP/KAN-66-playmode.xml" --timeout 900
```

EditMode에서 반경 경계, 무효 거리, 이미 소유한 ID, 선택 보류·취소·재시도, 기존 선택을 무효화하는 가방 변경을 검증한다. PlayMode에서 격리된 2D 물리 씬의 고정 스텝으로 전리품 루트 경계, 두 자식 콜라이더, 재진입과 획득 후 월드 객체 존속, 선택 알림만으로 받은 객체의 확정·취소를 재현한다. [Unity PhysicsScene2D.Simulate](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/physicsscene2d/simulate)


## 이번 작업의 검증 결과

2026-11-23 Unity 6000.6.9f1과 Unity CLI 1.0.1에서 EditMode 8건, 실제 2D 콜라이더를 사용하는 PlayMode 2건이 모두 통과했다. 두 결과 XML과 로그는 로컬 `%TEMP%/ProjectWeapon-KAN66-final-1795434329`에 남겼다.
