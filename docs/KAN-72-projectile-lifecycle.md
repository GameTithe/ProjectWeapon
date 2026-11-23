# KAN-72 공통 투사체 수명과 충돌

`ProjectileLifecycle`은 발사, 직선 이동, 접촉 접수, 종료, 풀 반환의 상태를 관리한다. 어댑터는 발사마다 새 적용 GUID를 발급해 원점·월드 속도·수명·피해량·표적 접촉 시 동작과 함께 `TryLaunch`에 전달한다. 수명은 양수이고 좌표·속도·피해량은 유한해야 한다. 피해량은 0 이상이다. [KAN-72](https://gametithe.atlassian.net/browse/KAN-72)

- `Advance`는 해당 발사의 시작 이후 경과 시간을 받으며, 발사 직후의 경과 시간은 0이다. 같은 시점을 다시 보내도 이동을 누적하지 않는다. 정확한 만료 시점에서는 충돌을 수락하지 않고 원점과 속도로 계산한 최종 위치에서 종료한다.
- 어댑터가 확인한 표적 접촉은 `ContactTarget`에 전달한다. 공격자와 사망·무효 표적을 제외하고, 같은 적용 GUID와 표적 ID 조합의 피해 요청은 한 건만 반환한다. 첫 표적에서 종료할지 다른 표적까지 계속할지는 발사 설정으로 공급한다. 확인된 차단 지형은 `ContactWorld`로 전달하면 종료한다.
- 풀에 반환할 때 `ResetForPool`로 적용 GUID, 수명, 경과 시간, 위치, 속도, 적중 기록을 지운다. 예약된 접촉에는 콜백을 생성한 시점의 적용 GUID를 붙인다. 이전 발사의 신호는 새 발사를 건드리지 않는다.
- 반환값 `DamageRequest`는 요청 자료다. 피해 적용과 HP 변경은 수신 측 경계에서 처리한다. 무기별 속도·수명·관통 설정은 호출부에서 공급한다. [KAN-70](https://gametithe.atlassian.net/browse/KAN-70) · [KAN-77](https://gametithe.atlassian.net/browse/KAN-77) · [KAN-130](https://gametithe.atlassian.net/browse/KAN-130)

## Unity 2D 연동 경계

Unity 2D 어댑터는 `Rigidbody2D`와 트리거 `Collider2D`를 사용하는 임시 실험 장면에서 확인할 수 있다. 트리거 쌍 중 하나에는 Rigidbody2D가 필요하다. `OnTriggerEnter2D`를 수신하면 부착 콜라이더를 표적의 안정 ID와 중심점으로 정규화하거나, 차단 지형으로 판별한다. 무관한 콜라이더는 전달하지 않는다. [Unity 6.6 Collider 2D](https://docs.unity.com/en-us/engine/6000.6/manual/unity2d/2d-physics/collider-2d) · [Unity 6.6 OnTriggerEnter2D](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/monobehaviour/ontriggerenter2d)

1. 고정 스텝의 위치와 접촉을 같은 시계의 발사 후 경과 시간으로 처리한다. 도메인 위치를 물리 어댑터의 이동 입력으로 쓰고, 접촉 콜백에서는 스텝의 실제 경과 시간과 접촉을 만든 발사의 GUID로 접수한다.
2. 동일 표적 GameObject에 트리거가 두 개 있어도 두 접촉을 같은 `TargetCandidate.TargetId`로 전달한다. 관통 설정에서는 다음 표적이 별도 피해 요청을 받을 수 있다.
3. 차단 지형, 첫 적중, 수명 만료로 활성 상태가 끝나면 어댑터에서 콜라이더를 비활성화하고 풀에 반환한다. 컴포넌트의 타이머나 속도도 새 발사에서 다시 설정한다.

## 자동 재현

```powershell
unity test . --mode EditMode --filter ProjectWeapon.Combat.Tests.ProjectileLifecycleTests --output "$env:TEMP/KAN-72-results.xml"
```

| 조건 | 기대 결과 |
| --- | --- |
| 수명 1초, 원점 `(2, 4)`, 초당 속도 `(8, -4)`, 250ms 진행 | 위치 `(4, 3)`이고 활성 상태다. 같은 경과 시간을 다시 줘도 위치가 같다. |
| 관통 발사에서 같은 실물 ID의 접촉 2건, 다른 ID의 접촉 1건 | 서로 다른 두 표적에 각각 피해 요청 한 건을 반환한다. |
| 충돌 시점 999ms와 1,000ms, 수명 1초 | 999ms 접촉은 유효하다. 1,000ms에는 수명이 끝나 피해 요청이 없다. |
| 재사용 후 같은 표적 접촉과 이전 적용 GUID의 지형 접촉 | 새 GUID의 표적은 다시 적중한다. 이전 적용 GUID는 상태를 바꾸지 않는다. |
| 비어 있는 적용 GUID, 잘못된 수명·좌표·피해량 | 발사를 거부하고 비활성 상태를 유지한다. |
