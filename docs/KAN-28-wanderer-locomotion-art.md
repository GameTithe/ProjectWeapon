# KAN-28 유랑 검사 대기·이동·회피

은빛 머리, 금속 어깨 보호대, 붉은 스카프와 망토의 유랑 검사 A를 픽셀 단위로 그렸다. 대기 2장, 이동 6장, 회피 4장을 남·동·북·서 방향마다 사용한다. 모든 프레임은 32×48px, 32 PPU, Point 필터, (16,6) 발 피벗으로 공격 스프라이트와 정렬된다. 편집 원본은 `art-source/Export-WandererLocomotion.ps1`과 `WandererPixelTools.ps1`이다. [KAN-21](https://gametithe.atlassian.net/browse/KAN-21) · [KAN-28](https://gametithe.atlassian.net/browse/KAN-28) · [KAN-196](https://gametithe.atlassian.net/browse/KAN-196)

캐릭터의 SpriteRenderer 오브젝트에 `WandererLocomotionView`를 붙이고 `WandererLocomotion.asset`을 지정한다. 이동을 샘플링한 뒤 매 프레임 `Present(move, dodging, dodgeDirection, deltaSeconds)`을 호출한다. 대각선은 절댓값이 큰 축을 쓰고 동률이면 이전 축을 유지한다. 정지 시 이전 방향의 대기 첫 프레임으로 바뀐다. 회피는 잠긴 방향을 사용하고 마지막 프레임에서 멈추다가 모션이 끝나면 해당 이동 또는 대기로 전환한다. 테스트용 속도는 대기 2, 이동 12, 회피 20프레임/초다. [KAN-46](https://gametithe.atlassian.net/browse/KAN-46) · [KAN-56](https://gametithe.atlassian.net/browse/KAN-56) · [KAN-197](https://gametithe.atlassian.net/browse/KAN-197)

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./art-source/Export-WandererLocomotion.ps1
unity test . --mode EditMode --filter ProjectWeapon.Art.Tests --output "$env:TEMP/KAN-28-editmode.xml"
```
