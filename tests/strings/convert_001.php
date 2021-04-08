<?php
namespace strings\convert_001;

function foo(string $x = null)
{
  $x = (string)$x;
  echo (int)('' === $x);
}

foo();
