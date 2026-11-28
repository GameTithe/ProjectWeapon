# KAN-196 유랑 검사 기본 공격과 낡은 검

유랑 검사 A의 은빛 머리, 붉은 스카프와 망토, 어두운 가죽과 금속 어깨 보호대를 픽셀 단위로 그렸다. 낡은 검은 이가 빠진 철제 날과 가죽 손잡이를 사용한다. 두 이미지의 제작 원본과 팔레트는 `art-source/Export-WandererAttack.ps1`에 있다. 프로젝트의 제공 콘셉트 아트를 참고해 새로 그렸다. [KAN-21](https://gametithe.atlassian.net/browse/KAN-21) · [KAN-196](https://gametithe.atlassian.net/browse/KAN-196)

| 에셋 | 프레임 | 피벗 |
| --- | --- | --- |
| `WandererAttack.png` | 32×48px 셀, 남·동·북·서 순서, 준비·들기·타격·회수 4장 | 셀 왼쪽 아래 기준 (16, 6) |
| `WornSword.png` | 32×32px | 손잡이 중앙 (9.5, 6.5) |

SpriteImporter에 32 PPU, Point, 압축 없음, 밉맵 끔을 지정했다. 공격은 세 번째 프레임을 실제 타격 시점에 맞춘다. 검은 별도 SpriteRenderer에 넣고 아래 위치에서 손잡이 피벗을 정렬한다. 북쪽 타격 프레임에서는 검을 캐릭터 몸 뒤로 정렬한다. 아래 표는 프레임 왼쪽 위의 픽셀 좌표다. Unity 로컬 위치는 `((x - 16) / 32, (42 - y) / 32)`이다.

| 방향 | 준비 | 들기 | 타격 | 회수 |
| --- | --- | --- | --- | --- |
| 남 | (8, 25) | (9, 17) | (26, 26) | (23, 31) |
| 동 | (14, 24) | (18, 17) | (28, 22) | (26, 28) |
| 북 | (20, 28) | (23, 17) | (28, 14) | (11, 22) |
| 서 | (17, 24) | (13, 17) | (3, 22) | (5, 28) |

아래 순서로 PNG를 다시 내보내고, 프레임 중복·여백 및 임포트된 셀과 피벗을 검증한다. 가방과 던전 씬에서의 검 장착 및 공격 재생은 한 방 데모에서 연결한다. [KAN-197](https://gametithe.atlassian.net/browse/KAN-197)

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./art-source/Export-WandererAttack.ps1
unity test . --mode EditMode --filter ProjectWeapon.Art.Tests --output "$env:TEMP/KAN-196-editmode.xml"
```
