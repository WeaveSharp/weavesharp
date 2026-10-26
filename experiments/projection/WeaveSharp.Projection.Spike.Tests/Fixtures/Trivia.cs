#define PICK_LEFT
class Whitespace
{
    void Run()
    {
        // Café 🧭 uses more UTF-16 units than Unicode scalar values.
#region A small calculation
#if PICK_LEFT
        Write(  1 /* retain this gap */ + 2 );
#else
        This branch is intentionally inactive text.
#endif
#endregion
        ; // An empty statement still has an owner.
    }
}
