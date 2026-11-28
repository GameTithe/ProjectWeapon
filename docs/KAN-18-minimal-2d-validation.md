# KAN-18 최소 2D 검증 씬

`Assets/Scenes/Validation2D/Validation2D.unity`를 열면 직교 카메라, 전역 2D 조명, Grid의 Tilemap과 별도 SpriteRenderer를 확인할 수 있다. 프로젝트의 Unity 6000.6.9f1, URP 17.6.0, Tilemap 1.0.0 구성을 사용한다. [KAN-18](https://gametithe.atlassian.net/browse/KAN-18) · [KAN-16](https://gametithe.atlassian.net/browse/KAN-16)

| 구성 | 설정 |
| --- | --- |
| 카메라 | 직교 투영, 크기 2.75, 단색 배경, 프로젝트 기본 2D 렌더러 |
| 조명 | 기본 정렬 레이어의 전역 2D 조명, 강도 1 |
| 타일맵 | 7×4 셀, 1유닛 Grid, 석재 및 가장자리 타일, 2D Lit 머티리얼 |
| 독립 스프라이트 | 바닥보다 앞에 그리는 포털 표식, 2D Lit 머티리얼 |
| 검증 텍스처 | 32×32px, 32px/unit, Point 필터, 압축과 밉맵 해제 |

씬과 검증 에셋은 같은 폴더에 있다. Unity 에디터에서 직접 열어 검증할 수 있다.

## 재현

1. 위 씬을 열고 Game 뷰를 16:9로 설정한다.
2. Play 버튼을 누른다. 색이 다른 가장자리를 포함한 7×4의 석재 바닥과 가운데 파란 포털 표식이 함께 표시되는지 확인한다.
3. Play 버튼을 다시 눌러 편집 모드로 돌아간다. Console에서 플레이 시작과 종료 과정에 오류가 없는지 확인한다.

색상은 프로젝트의 던전 콘셉트에서 가져왔으며 월드 스케일과 임포트 값은 1유닛 타일에 대한 확정 규격을 따른다. [KAN-21](https://gametithe.atlassian.net/browse/KAN-21)

Unity 6000.6.9f1의 Play Mode에서 `Renderer2D`가 28개 타일과 독립 포털 스프라이트를 출력했다. 프레임 카운터 증가를 확인하고 960×540 카메라 캡처로 두 렌더러의 결과를 대조했다. Play Mode 종료 후 편집 모드로 돌아왔으며 Console 오류는 0건이었다.
