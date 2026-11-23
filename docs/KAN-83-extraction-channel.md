# KAN-83 3초 귀환 채널링

`ExtractionAccess.TryStart`가 수락한 활성 시도를 `ExtractionChannel`로 진행한다. 각 `RunState` 인스턴스에 대해 하나의 채널 인스턴스를 유지한다. `RunState`가 결정한 시도 시작·완료 예정 시각을 그대로 사용한다. [KAN-75](https://gametithe.atlassian.net/browse/KAN-75) · [KAN-188](https://gametithe.atlassian.net/browse/KAN-188) · [KAN-83](https://gametithe.atlassian.net/browse/KAN-83)

1. 호출부는 같은 프레임에 관찰한 피해·사망 등 기존 `RunSignal`을 모아 `ProcessFrame`에 한 번 전달한다. 관찰이 없을 때도 빈 목록으로 호출한다. 이 메서드가 해당 프레임의 RunState 판정을 실행한다.
2. 3초가 지났으면 예정 완료 시각의 성공 요청을 같은 프레임에 포함한다. 같은 런과 시도에서는 요청을 한 번만 전달한다. 호출부의 목록은 그대로 유지한다. 프레임 요청을 읽는 중 실패하면 실제 판정 전에는 요청을 다시 전달할 수 있다.
3. `ProgressChanged`는 수락된 시도의 ID와 0~1 진행률을 전달한다. 진행률은 0초에 0, 1.5초에 0.5, 성공에 1이다. 취소나 실패 시 값이 비며, 성공 값은 해당 결과가 활성인 동안 유지된다.
4. 자동공격 호출부는 기존 발동 조건과 함께 `IsAutomaticAttackSuppressed`를 확인한다. 귀환 중인 단계에서만 참이며, 귀환 결과나 취소를 반영한 직후 거짓이 된다. [KAN-54](https://gametithe.atlassian.net/browse/KAN-54)

같은 프레임에 사망·완료·만료가 들어오면 한 시계 표본으로 기존 RunState의 경합 규칙을 적용한다. 결과 통지와 정산은 계속 `RunResultFinalized`를 사용한다. 기존 VFX에는 시작·취소·결과를 연결할 수 있다. 이동·피해로 취소 신호를 만드는 규칙은 관련 결정과 후속 작업에서 연결한다. [KAN-19](https://gametithe.atlassian.net/browse/KAN-19) · [KAN-30](https://gametithe.atlassian.net/browse/KAN-30) · [KAN-81](https://gametithe.atlassian.net/browse/KAN-81) · [KAN-190](https://gametithe.atlassian.net/browse/KAN-190)

## 재현 절차

```powershell
unity test . --mode EditMode --filter ProjectWeapon.Extraction.Tests.ExtractionChannelTests --output "$env:TEMP/KAN-83-results.xml"
```

| 입력 | 기대 결과 |
| --- | --- |
| 귀환 시작에서 0, 1.5, 2.999, 3.000초 순서로 호출 | 앞선 프레임에서는 진행률만 제공하며 완료 시점에 한 번 성공한다. |
| 6.999초에 시작한 뒤 10.001초로 프레임을 건너뜀. 만료는 10초 | 완료 시각 9.999초의 성공을 판정한다. |
| 귀환 완료와 사망 유효 시점이 같은 프레임에 들어옴 | 먼저 도달한 허용 결과를 RunState가 판정한다. |
| 기존 취소 신호를 받은 뒤 귀환을 새로 시작함 | 진행률이 지워지고 자동공격 제한이 해제된다. 새 시도의 진행률은 다시 0부터 시작한다. |
| 완료 프레임 목록을 읽는 중 예외가 난 뒤 빈 목록으로 다시 호출 | 같은 시도의 완료를 정상 판정하며 결과는 한 번 확정된다. |
