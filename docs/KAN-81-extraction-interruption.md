# KAN-81 이동·피해에 의한 귀환 중단

귀환 버튼의 활성 여부 및 입력 처리는 `ExtractionAccess.CanStart`와 `TryStart`에 수락한 입력의 크기를 전달한다. KAN-56 어댑터를 쓰면 `movement.SampleDirection().magnitude`로 물리 이동 전 입력을 읽는다. 0.20 이상이면 시작을 보류한다. 실제 위치 변화량에는 의존하지 않으므로 벽에 기대고 이동을 눌러도 적용된다. [KAN-81](https://gametithe.atlassian.net/browse/KAN-81) · [KAN-190](https://gametithe.atlassian.net/browse/KAN-190) · [KAN-56](https://gametithe.atlassian.net/browse/KAN-56)

활성 채널링의 프레임 처리 순서

1. 입력 샘플과 승인된 회피 이벤트의 탐험 ID·유효 시각을 읽는다. `ExtractionChannel.ObserveMovement`와 `ObserveAcceptedDodge`가 반환한 취소 신호를 이 프레임 목록에 추가한다. 회피 버튼을 눌렀으나 쿨다운 등의 이유로 요청이 거절됐으면 회피 신호를 추가하지 않는다.
2. 같은 프레임의 유효 피해는 `PlayerRunDamage.ApplyHit`에 전달해 같은 목록에 합친다. KAN-56의 플레이어 회피를 함께 연결할 때는 `dodge: movement.Dodge`를 전달한다.
3. 목록을 모아 `ExtractionChannel.ProcessFrame`에 한 번 전달한다. 시계와 취소·사망·완료의 유효 시각에 따라 기존 RunState가 판정한다.
4. 이후 자동공격을 검사한다. 채널링이 취소되면 자동공격의 기존 ON/OFF 값이 복원된다.

아래 검증은 0.20 경계와 입력 중 시작 보류, 무변위 취소, 유효·오래된 회피 요청, 피해·완료와 만료의 동시 경합, 자동공격 ON/OFF 복원을 재현한다.

```powershell
unity test . --mode EditMode --filter ProjectWeapon.Extraction.Tests.ExtractionInterruptionTests --output "$env:TEMP/KAN-81-editmode.xml"
```
